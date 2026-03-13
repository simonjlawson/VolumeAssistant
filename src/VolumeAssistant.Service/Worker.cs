using Microsoft.Extensions.Options;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;
using VolumeAssistant.Service.Matter;
using VolumeAssistant.Service.Matter.Clusters;

namespace VolumeAssistant.Service;

/// <summary>
/// Main background worker service that coordinates:
/// 1. Windows audio volume monitoring
/// 2. Cambridge Audio amplifier control (optional)
/// 3. Matter device state synchronization
/// 4. Matter server operation (optional)
/// 5. mDNS advertisement
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly IAudioController _audioController;
    private readonly ICambridgeAudioClient? _cambridgeAudio;
    private readonly CambridgeAudioOptions _cambridgeOptions;
    private readonly MatterDevice _matterDevice;
    private readonly MatterServer _matterServer;
    private readonly MdnsAdvertiser _mdnsAdvertiser;
    private readonly MatterOptions _matterOptions;
    private readonly ILogger<Worker> _logger;
    
    // Counter to suppress Windows-originated volume change handling when the service
    // itself is applying a change to the Windows volume. Use an integer counter
    // to support nested suppressions across async boundaries.
    private int _suppressWindowsVolumeChangeCount;
    // Track last known Windows volume percent so we can compute relative deltas
    // when RelativeVolume is enabled.
    private float _lastWindowsVolumePercent;
    // Encapsulated syncer for Windows->CambridgeAudio updates
    private CambridgeAudioSyncer? _cambridgeSyncer;
    // Handler delegate used to add/remove SystemEvents subscription via reflection
    private Delegate? _powerModeHandler;
    // Optional media key listener for forwarding OS media keys to Cambridge Audio
    private MediaKeyListener? _mediaKeyListener;

    public Worker(
        IAudioController audioController,
        MatterDevice matterDevice,
        MatterServer matterServer,
        MdnsAdvertiser mdnsAdvertiser,
        ILogger<Worker> logger,
        ICambridgeAudioClient? cambridgeAudio = null,
        IOptions<CambridgeAudioOptions>? cambridgeOptions = null,
        IOptions<MatterOptions>? matterOptions = null)
    {
        _audioController = audioController;
        _matterDevice = matterDevice;
        _matterServer = matterServer;
        _mdnsAdvertiser = mdnsAdvertiser;
        _logger = logger;
        _cambridgeAudio = cambridgeAudio;
        _cambridgeOptions = cambridgeOptions?.Value ?? new CambridgeAudioOptions();
        _matterOptions = matterOptions?.Value ?? new MatterOptions();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VolumeAssistant service starting.");

        // Initialize device state from current Windows volume
        float initialVolume = _audioController.GetVolumePercent();
        bool initialMuted = _audioController.GetMuted();
        if (_matterOptions.Enabled)
        {
            _matterDevice.UpdateFromVolume(initialVolume, initialMuted);
        }

        // initialize last-known Windows volume for relative calculations
        _lastWindowsVolumePercent = initialVolume;

        _logger.LogInformation(
            "Initial volume: {Volume:F1}%, Muted: {Muted}",
            initialVolume, initialMuted);

        // Subscribe to Windows volume change events
        _audioController.VolumeChanged += OnWindowsVolumeChanged;

        // Subscribe to system power mode changes (use reflection to avoid a
        // hard dependency on Microsoft.Win32.SystemEvents types at compile time).
        if (_cambridgeAudio != null)
        {
            var powerEvent = Type.GetType("Microsoft.Win32.SystemEvents, Microsoft.Win32.SystemEvents")?.GetEvent("PowerModeChanged");
            if (powerEvent != null)
            {
                var handlerMethod = typeof(Worker).GetMethod("OnPowerModeChangedInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                _powerModeHandler = Delegate.CreateDelegate(powerEvent.EventHandlerType!, this, handlerMethod);
                powerEvent.AddEventHandler(null, _powerModeHandler);
            }
        }

        // Subscribe to Matter device state changes (from Matter controller commands)
        if (_matterOptions.Enabled)
        {
            _matterDevice.DeviceStateChanged += OnMatterDeviceStateChanged;

            // Start the Matter UDP server
            _matterServer.Start(stoppingToken);

            // Start mDNS advertisement
            _mdnsAdvertiser.Start();

            _logger.LogInformation(
                "VolumeAssistant Matter device ready. Instance: {InstanceName}, Discriminator: {Discriminator}",
                _matterDevice.InstanceName, _matterDevice.Discriminator);
        }

        // Start Cambridge Audio integration if configured
        if (_cambridgeAudio != null)
        {
            _cambridgeAudio.ConnectionChanged += OnCambridgeAudioConnectionChanged;

            // Create syncer for coalescing rapid Windows volume changes
            _cambridgeSyncer = new CambridgeAudioSyncer(_cambridgeAudio, _cambridgeOptions, _logger);

            // ConnectAsync runs the reconnect loop in the background
            _ = Task.Run(async () =>
            {
                await _cambridgeAudio.ConnectAsync(stoppingToken).ConfigureAwait(false);

                // After successful connect, apply optional startup settings
                try
                {
                    if (_cambridgeOptions.StartPower)
                    {
                        try
                        {
                            await _cambridgeAudio.PowerOnAsync(stoppingToken).ConfigureAwait(false);
                            _logger.LogInformation("Cambridge Audio powered on (StartPower enabled).");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to power on Cambridge Audio device on startup.");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(_cambridgeOptions.StartSourceName))
                    {
                        // Find source by name
                        var sources = _cambridgeAudio.Sources;
                        var match = sources.FirstOrDefault(s => s.Name.Equals(_cambridgeOptions.StartSourceName, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            await _cambridgeAudio.SetSourceAsync(match.Id);
                            _logger.LogInformation($"Cambridge Audio Source Set - Source: {_cambridgeOptions.StartSourceName}");
                        }
                    }

                    if (_cambridgeOptions.MaxVolume.HasValue)
                    {
                        _logger.LogInformation($"Cambridge Audio MaxVolume scaling in effect - MaxVolume: {_cambridgeOptions.MaxVolume.Value}");
                    }

                    if (_cambridgeOptions.StartVolume.HasValue)
                    {
                        await _cambridgeAudio.SetVolumeAsync(_cambridgeOptions.StartVolume.Value);
                        _logger.LogInformation($"Cambridge Audio Volume Set - Volume: {_cambridgeOptions.StartVolume}");
                    }

                    if (!string.IsNullOrWhiteSpace(_cambridgeOptions.StartOutput))
                    {
                        // audio_output is an optional parameter; use the new API to set it
                        await _cambridgeAudio.SetAudioOutputAsync(_cambridgeOptions.StartOutput!);
                        _logger.LogInformation($"Cambridge Audio Output Set - Output: {_cambridgeOptions.StartOutput}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to apply Cambridge Audio startup settings.");
                }
            }, stoppingToken);

            _logger.LogInformation("Cambridge Audio integration ready!");

            // Start media key listener if enabled
            if (_cambridgeOptions.MediaKeysEnabled)
            {
                _mediaKeyListener = new MediaKeyListener();
                _mediaKeyListener.PlayPausePressed += OnMediaKeyPlayPause;
                _mediaKeyListener.NextTrackPressed += OnMediaKeyNextTrack;
                _mediaKeyListener.PreviousTrackPressed += OnMediaKeyPreviousTrack;
                _mediaKeyListener.Start();
                _logger.LogInformation("Media key listener started (Play/Pause, Next, Previous forwarded to Cambridge Audio).");
            }
        }

        // Keep alive until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }

        // Cleanup
        _audioController.VolumeChanged -= OnWindowsVolumeChanged;
        if (_matterOptions.Enabled)
        {
            _matterDevice.DeviceStateChanged -= OnMatterDeviceStateChanged;
        }

            if (_cambridgeAudio != null)
            {
                _cambridgeAudio.ConnectionChanged -= OnCambridgeAudioConnectionChanged;
                if (_cambridgeSyncer != null)
                {
                    await _cambridgeSyncer.DisposeAsync();
                    _cambridgeSyncer = null;
                }

                // Unsubscribe from system power events if we created a handler
                if (_powerModeHandler != null)
                {
                    var powerEvent = Type.GetType("Microsoft.Win32.SystemEvents, Microsoft.Win32.SystemEvents")?.GetEvent("PowerModeChanged");
                    powerEvent?.RemoveEventHandler(null, _powerModeHandler);
                    _powerModeHandler = null;
                }

                if (_cambridgeOptions.ClosePower)
                {
                    try
                    {
                        await _cambridgeAudio.PowerOffAsync().ConfigureAwait(false);
                        _logger.LogInformation("Cambridge Audio powered off (ClosePower enabled).");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to power off Cambridge Audio device on shutdown.");
                    }
                }

                await _cambridgeAudio.DisconnectAsync();

                // Dispose media key listener
                if (_mediaKeyListener != null)
                {
                    _mediaKeyListener.PlayPausePressed -= OnMediaKeyPlayPause;
                    _mediaKeyListener.NextTrackPressed -= OnMediaKeyNextTrack;
                    _mediaKeyListener.PreviousTrackPressed -= OnMediaKeyPreviousTrack;
                    _mediaKeyListener.Dispose();
                    _mediaKeyListener = null;
                }
            }

        if (_matterOptions.Enabled)
        {
            _mdnsAdvertiser.Stop();
            _matterServer.Stop();
        }

        _logger.LogInformation("VolumeAssistant service stopped.");
    }

    /// <summary>
    /// Handles Windows volume changes – propagates them to Matter subscribers
    /// and optionally synchronises the Cambridge Audio amplifier volume.
    /// </summary>
    private void OnWindowsVolumeChanged(object? sender, VolumeChangedEventArgs e)
    {
        _logger.LogInformation(
            "Windows volume changed: {Volume:F1}%, Muted: {Muted}",
            e.VolumePercent, e.IsMuted);

        byte matterLevel = LevelControlCluster.VolumePercentToMatterLevel(e.VolumePercent);

        // Update device state without triggering Windows volume change again
        _matterDevice.LevelControlCluster.CurrentLevel = matterLevel;
        _matterDevice.OnOffCluster.OnOff = !e.IsMuted;

        _ = Task.Run(async () =>
        {
            // Notify Matter subscribers
            if (_matterOptions.Enabled)
            {
                await _matterServer.NotifySubscribersAsync(1, ClusterId.LevelControl, 0x0000, matterLevel);
                await _matterServer.NotifySubscribersAsync(1, ClusterId.OnOff, 0x0000, !e.IsMuted);
            }

            // Sync to Cambridge Audio amplifier
            // If we are currently suppressing Windows-originated events, skip syncing
            // back to Cambridge Audio – this avoids feedback loops when the service
            // applied the Windows volume itself.
            if (Volatile.Read(ref _suppressWindowsVolumeChangeCount) == 0
                && _cambridgeAudio != null && _cambridgeAudio.IsConnected)
            {
                try
                {
                    int? desiredCamVolume = null;
                    bool desiredMute = e.IsMuted;

                    if (_cambridgeOptions.RelativeVolume)
                    {
                        float delta = e.VolumePercent - _lastWindowsVolumePercent;
                        if (Math.Abs(delta) >= 0.5f)
                        {
                            var camState = _cambridgeAudio.State;
                            if (camState?.VolumePercent.HasValue == true)
                            {
                                desiredCamVolume = (int)Math.Round(camState.VolumePercent.Value + delta);
                            }
                            else
                            {
                                desiredCamVolume = (int)Math.Round(WindowsToCambridgeVolume(e.VolumePercent));
                            }
                            desiredCamVolume = Math.Clamp(desiredCamVolume.Value, 0, 100);
                        }
                    }
                    else
                    {
                        desiredCamVolume = (int)Math.Round(WindowsToCambridgeVolume(e.VolumePercent));
                    }

                    if (desiredCamVolume.HasValue || desiredMute != null)
                    {
                        _cambridgeSyncer?.Enqueue(desiredCamVolume, desiredMute);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to queue Windows->Cambridge Audio sync.");
                }
            }

            // Update last known Windows volume for future relative calculations
            _lastWindowsVolumePercent = e.VolumePercent;
        });
    }


    /// <summary>
    /// Handles Matter device state changes (from Matter controller commands)
    /// and applies them to Windows volume and optionally to the Cambridge Audio amplifier.
    /// </summary>
    private void OnMatterDeviceStateChanged(object? sender, (byte Level, bool IsOn) state)
    {
        float volumePercent = LevelControlCluster.MatterLevelToVolumePercent(state.Level);
        bool muted = !state.IsOn;

        _logger.LogInformation(
            "Matter command received: Level={Level} ({Volume:F1}%), Muted: {Muted}",
            state.Level, volumePercent, muted);

        // Suppress the Windows volume changed handler while applying the change
        // because we will explicitly sync to Cambridge Audio below.
        Interlocked.Increment(ref _suppressWindowsVolumeChangeCount);
        try
        {
            _audioController.SetVolumePercent(volumePercent);
            _audioController.SetMuted(muted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply volume change from Matter command.");
        }
        finally
        {
            Interlocked.Decrement(ref _suppressWindowsVolumeChangeCount);
        }

        // Sync to Cambridge Audio amplifier
        if (_cambridgeAudio != null && _cambridgeAudio.IsConnected)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    int volumeInt = (int)Math.Round(volumePercent);
                    await _cambridgeAudio.SetVolumeAsync(volumeInt);
                    await _cambridgeAudio.SetMuteAsync(muted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync Matter volume command to Cambridge Audio device.");
                }
            });
        }
    }

    /// <summary>
    /// Handles Cambridge Audio state changes and syncs volume/mute back to Windows.
    /// This allows controlling Windows volume by turning the Cambridge Audio volume knob.
    /// </summary>
    private void OnCambridgeAudioStateChanged(object? sender, CambridgeAudioStateChangedEventArgs e)
    {
        var state = e.State;

        _logger.LogInformation(
            "Cambridge Audio state changed: Source={Source}, Volume={Volume}%, Muted={Muted}",
            state.Source, state.VolumePercent, state.Mute);

        if (state.VolumePercent.HasValue)
        {
            // Suppress Windows volume change handling while we apply the change so
            // the resulting Windows event does not re-sync back to Cambridge Audio.
            Interlocked.Increment(ref _suppressWindowsVolumeChangeCount);
            try
            {
                _audioController.SetVolumePercent(CambridgeToWindowsVolume(state.VolumePercent.Value));
                _audioController.SetMuted(state.Mute);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply Cambridge Audio volume change to Windows.");
            }
            finally
            {
                Interlocked.Decrement(ref _suppressWindowsVolumeChangeCount);
            }
        }
    }

    private void OnCambridgeAudioConnectionChanged(
        object? sender, CambridgeAudioConnectionChangedEventArgs e)
    {
        _logger.LogInformation(
            "Cambridge Audio device connection state: {State}",
            e.IsConnected ? "Connected" : "Disconnected");
    }

    /// <summary>
    /// Handles the Play/Pause media key press and forwards it to the Cambridge Audio device.
    /// </summary>
    private void OnMediaKeyPlayPause(object? sender, EventArgs e)
    {
        if (_cambridgeAudio == null || !_cambridgeAudio.IsConnected) return;
        _logger.LogInformation("Media key: Play/Pause → Cambridge Audio");
        _ = Task.Run(async () =>
        {
            try { await _cambridgeAudio.PlayPauseAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                // If the device reports a 400 for play control requests, this typically
                // means the currently selected source doesn't support media key actions.
                // Log as information rather than an error so it doesn't spam the console.
                if (ex is VolumeAssistant.Service.CambridgeAudio.CambridgeAudioException &&
                    ex.Message.Contains("error 400", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Play/Pause - Not possible from this source");
                }
                else
                {
                    _logger.LogWarning(ex, "Failed to send Play/Pause to Cambridge Audio.");
                }
            }
        });
    }

    /// <summary>
    /// Handles the Next Track media key press and forwards it to the Cambridge Audio device.
    /// </summary>
    private void OnMediaKeyNextTrack(object? sender, EventArgs e)
    {
        if (_cambridgeAudio == null || !_cambridgeAudio.IsConnected) return;
        _logger.LogInformation("Media key: Next Track → Cambridge Audio");
        _ = Task.Run(async () =>
        {
            try { await _cambridgeAudio.NextTrackAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                if (ex is VolumeAssistant.Service.CambridgeAudio.CambridgeAudioException &&
                    ex.Message.Contains("error 400", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Next Track - Not possible from this source");
                }
                else
                {
                    _logger.LogWarning(ex, "Failed to send Next Track to Cambridge Audio.");
                }
            }
        });
    }

    /// <summary>
    /// Handles the Previous Track media key press and forwards it to the Cambridge Audio device.
    /// </summary>
    private void OnMediaKeyPreviousTrack(object? sender, EventArgs e)
    {
        if (_cambridgeAudio == null || !_cambridgeAudio.IsConnected) return;
        _logger.LogInformation("Media key: Previous Track → Cambridge Audio");
        _ = Task.Run(async () =>
        {
            try { await _cambridgeAudio.PreviousTrackAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                if (ex is VolumeAssistant.Service.CambridgeAudio.CambridgeAudioException &&
                    ex.Message.Contains("error 400", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Previous Track - Not possible from this source");
                }
                else
                {
                    _logger.LogWarning(ex, "Failed to send Previous Track to Cambridge Audio.");
                }
            }
        });
    }

    // Internal handler invoked via a reflection-created delegate. We intentionally
    // use EventArgs to avoid a compile-time dependency on Microsoft.Win32 types.
    private void OnPowerModeChangedInternal(object? sender, EventArgs e)
    {
        // Run asynchronously to avoid blocking the event thread.
        _ = Task.Run(async () =>
        {
            try
            {
                // Inspect the 'Mode' property of the event args using reflection so
                // we don't need to reference the PowerModes enum at compile time.
                var modeProp = e?.GetType().GetProperty("Mode");
                var modeName = modeProp?.GetValue(e)?.ToString();

                if (modeName == "Resume" && _cambridgeAudio != null && _cambridgeOptions.StartPower)
                {
                    try
                    {
                        await _cambridgeAudio.PowerOnAsync().ConfigureAwait(false);
                        _logger.LogInformation("Cambridge Audio powered on (System resume, StartPower enabled).");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to power on Cambridge Audio device on system resume.");
                    }
                }

                if (modeName == "Suspend" && _cambridgeAudio != null && _cambridgeOptions.ClosePower)
                {
                    try
                    {
                        await _cambridgeAudio.PowerOffAsync().ConfigureAwait(false);
                        _logger.LogInformation("Cambridge Audio powered off (System suspend, ClosePower enabled).");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to power off Cambridge Audio device on system suspend.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error handling system power mode change.");
            }
        });
    }

    /// <summary>
    /// Scales a Windows volume percentage (0–100) to a Cambridge Audio volume percentage (0–100),
    /// applying <see cref="CambridgeAudioOptions.MaxVolume"/> scaling when configured.
    /// </summary>
    private float WindowsToCambridgeVolume(float windowsPercent)
    {
        if (_cambridgeOptions.MaxVolume.HasValue)
        {
            return (float)(windowsPercent / 100.0 * _cambridgeOptions.MaxVolume.Value);
        }
        return windowsPercent;
    }

    /// <summary>
    /// Scales a Cambridge Audio volume percentage (0–100) back to a Windows volume percentage (0–100),
    /// applying the inverse of <see cref="CambridgeAudioOptions.MaxVolume"/> scaling when configured.
    /// </summary>
    private float CambridgeToWindowsVolume(float cambridgePercent)
    {
        if (_cambridgeOptions.MaxVolume.HasValue)
        {
            if (_cambridgeOptions.MaxVolume.Value <= 0)
                return 0f;
            return (float)(cambridgePercent / _cambridgeOptions.MaxVolume.Value * 100.0);
        }
        return cambridgePercent;
    }
}
