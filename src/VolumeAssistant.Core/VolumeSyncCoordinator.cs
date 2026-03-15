using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;
using VolumeAssistant.Service.Matter;
using VolumeAssistant.Service.Matter.Clusters;

namespace VolumeAssistant.Service;

public sealed class VolumeSyncCoordinator
{
    private readonly IAudioController _audioController;
    private readonly ICambridgeAudioClient? _cambridgeAudio;
    private readonly CambridgeAudioOptions _cambridgeOptions;
    private readonly MatterDevice _matterDevice;
    private readonly MatterServer _matterServer;
    private readonly MdnsAdvertiser _mdnsAdvertiser;
    private readonly MatterOptions _matterOptions;
    private readonly ILogger _logger;

    private int _suppressWindowsVolumeChangeCount;
    private float _lastWindowsVolumePercent;
    private CambridgeAudioSyncer? _cambridgeSyncer;
    private Delegate? _powerModeHandler;
    private Delegate? _sessionEndingHandler;
    private MediaKeyListener? _mediaKeyListener;

    // Internal test seam: allow tests to set or get the syncer instance directly.
    internal CambridgeAudioSyncer? CambridgeSyncer
    {
        get => _cambridgeSyncer;
        set => _cambridgeSyncer = value;
    }

    public VolumeSyncCoordinator(
        IAudioController audioController,
        MatterDevice matterDevice,
        MatterServer matterServer,
        MdnsAdvertiser mdnsAdvertiser,
        ILogger logger,
        ICambridgeAudioClient? cambridgeAudio,
        CambridgeAudioOptions cambridgeOptions,
        MatterOptions matterOptions)
    {
        _audioController = audioController ?? throw new ArgumentNullException(nameof(audioController));
        _matterDevice = matterDevice ?? throw new ArgumentNullException(nameof(matterDevice));
        _matterServer = matterServer ?? throw new ArgumentNullException(nameof(matterServer));
        _mdnsAdvertiser = mdnsAdvertiser ?? throw new ArgumentNullException(nameof(mdnsAdvertiser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cambridgeAudio = cambridgeAudio;
        _cambridgeOptions = cambridgeOptions ?? new CambridgeAudioOptions();
        _matterOptions = matterOptions ?? new MatterOptions();
    }

    public Task StartAsync(CancellationToken stoppingToken)
    {
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

        // Subscribe to system power mode changes via reflection to avoid hard dependency
        if (_cambridgeAudio != null)
        {
            var powerEvent = Type.GetType("Microsoft.Win32.SystemEvents, Microsoft.Win32.SystemEvents")?.GetEvent("PowerModeChanged");
            if (powerEvent != null)
            {
                var handlerMethod = typeof(VolumeSyncCoordinator).GetMethod("OnPowerModeChangedInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                _powerModeHandler = Delegate.CreateDelegate(powerEvent.EventHandlerType!, this, handlerMethod);
                powerEvent.AddEventHandler(null, _powerModeHandler);
            }
            // Also subscribe to session ending (logoff / shutdown) so we can attempt
            // to power off the Cambridge Audio device when the system is shutting down.
            var sessionEvent = Type.GetType("Microsoft.Win32.SystemEvents, Microsoft.Win32.SystemEvents")?.GetEvent("SessionEnding");
            if (sessionEvent != null)
            {
                var sessionHandlerMethod = typeof(VolumeSyncCoordinator).GetMethod("OnSessionEndingInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                _sessionEndingHandler = Delegate.CreateDelegate(sessionEvent.EventHandlerType!, this, sessionHandlerMethod);
                sessionEvent.AddEventHandler(null, _sessionEndingHandler);
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
                _mediaKeyListener.SourceSwitchRequested += OnMediaKeySourceSwitchRequested;
                _mediaKeyListener.Start();
                _logger.LogInformation("Media key listener started (Play/Pause, Next, Previous forwarded to Cambridge Audio).");
            }
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
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
                await _cambridgeSyncer.DisposeAsync().ConfigureAwait(false);
                _cambridgeSyncer = null;
            }

            // Unsubscribe from system power events if we created a handler
            if (_powerModeHandler != null)
            {
                var powerEvent = Type.GetType("Microsoft.Win32.SystemEvents, Microsoft.Win32.SystemEvents")?.GetEvent("PowerModeChanged");
                powerEvent?.RemoveEventHandler(null, _powerModeHandler);
                _powerModeHandler = null;
            }
            if (_sessionEndingHandler != null)
            {
                var sessionEvent = Type.GetType("Microsoft.Win32.SystemEvents, Microsoft.Win32.SystemEvents")?.GetEvent("SessionEnding");
                sessionEvent?.RemoveEventHandler(null, _sessionEndingHandler);
                _sessionEndingHandler = null;
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

            await _cambridgeAudio.DisconnectAsync().ConfigureAwait(false);

            // Dispose media key listener
            if (_mediaKeyListener != null)
            {
                _mediaKeyListener.PlayPausePressed -= OnMediaKeyPlayPause;
                _mediaKeyListener.NextTrackPressed -= OnMediaKeyNextTrack;
                _mediaKeyListener.PreviousTrackPressed -= OnMediaKeyPreviousTrack;
                _mediaKeyListener.SourceSwitchRequested -= OnMediaKeySourceSwitchRequested;
                _mediaKeyListener.Dispose();
                _mediaKeyListener = null;
            }
        }

        if (_matterOptions.Enabled)
        {
            _mdnsAdvertiser.Stop();
            _matterServer.Stop();
        }
    }

    internal void OnWindowsVolumeChanged(object? sender, VolumeChangedEventArgs e)
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
                await _matterServer.NotifySubscribersAsync(1, ClusterId.LevelControl, 0x0000, matterLevel).ConfigureAwait(false);
                await _matterServer.NotifySubscribersAsync(1, ClusterId.OnOff, 0x0000, !e.IsMuted).ConfigureAwait(false);
            }

            // Sync to Cambridge Audio amplifier
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

    internal void OnMatterDeviceStateChanged(object? sender, (byte Level, bool IsOn) state)
    {
        float volumePercent = LevelControlCluster.MatterLevelToVolumePercent(state.Level);
        bool muted = !state.IsOn;

        _logger.LogInformation(
            "Matter command received: Level={Level} ({Volume:F1}%), Muted: {Muted}",
            state.Level, volumePercent, muted);

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

        if (_cambridgeAudio != null && _cambridgeAudio.IsConnected)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    int volumeInt = (int)Math.Round(volumePercent);
                    await _cambridgeAudio.SetVolumeAsync(volumeInt).ConfigureAwait(false);
                    await _cambridgeAudio.SetMuteAsync(muted).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync Matter volume command to Cambridge Audio device.");
                }
            });
        }
    }

    internal void OnSessionEndingInternal(object? sender, EventArgs e)
    {
        // SessionEnding occurs on logoff or shutdown. Attempt to power off the
        // Cambridge Audio device if configured. Use the same pattern as the
        // power mode handler to avoid a hard dependency on Microsoft.Win32 types.
        _ = Task.Run(async () =>
        {
            try
            {
                if (_cambridgeAudio != null && _cambridgeOptions.ClosePower)
                {
                    try
                    {
                        await _cambridgeAudio.PowerOffAsync().ConfigureAwait(false);
                        _logger.LogInformation("Cambridge Audio powered off (Session ending, ClosePower enabled).");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to power off Cambridge Audio device on session end.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error handling session end event.");
            }
        });
    }

    internal void OnCambridgeAudioStateChanged(object? sender, CambridgeAudioStateChangedEventArgs e)
    {
        var state = e.State;

        _logger.LogInformation(
            "Cambridge Audio state changed: Source={Source}, Volume={Volume}%, Muted={Muted}",
            state.Source, state.VolumePercent, state.Mute);

        if (state.VolumePercent.HasValue)
        {
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

    private void OnCambridgeAudioConnectionChanged(object? sender, CambridgeAudioConnectionChangedEventArgs e)
    {
        _logger.LogInformation(
            "Cambridge Audio device connection state: {State}",
            e.IsConnected ? "Connected" : "Disconnected");
    }

    private void OnMediaKeyPlayPause(object? sender, EventArgs e)
    {
        if (_cambridgeAudio == null || !_cambridgeAudio.IsConnected) return;
        _logger.LogInformation("Media key: Play/Pause → Cambridge Audio");
        _ = Task.Run(async () =>
        {
            try { await _cambridgeAudio.PlayPauseAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
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

    private void OnMediaKeySourceSwitchRequested(object? sender, EventArgs e)
    {
        if (_cambridgeAudio == null || !_cambridgeAudio.IsConnected) return;
        if (!_cambridgeOptions.SourceSwitchingEnabled) return;

        // Log an immediate informational entry so the UI can show a transient
        // popup right away (before the async operation completes).
        try
        {
            var target = TryGetNextTargetName();
            _logger.LogInformation("Source switch requested: {Target}", target ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to compute next source name for popup.");
        }

        _ = Task.Run(async () => await CycleSourceAsync().ConfigureAwait(false));
    }

    /// <summary>
    /// Attempts to compute the next configured source name synchronously so the UI
    /// can show a preview in a popup immediately when the user requests a source switch.
    /// Returns null when the target cannot be determined.
    /// </summary>
    private string? TryGetNextTargetName()
    {
        try
        {
            var namesRaw = _cambridgeOptions.SourceSwitchingNames;
            if (string.IsNullOrWhiteSpace(namesRaw)) return null;

            var configuredNames = namesRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (configuredNames.Length == 0) return null;

            var sources = _cambridgeAudio?.Sources;
            if (sources == null || sources.Count == 0) return null;

            var currentSourceId = _cambridgeAudio.State?.Source ?? string.Empty;
            var currentSource = sources.FirstOrDefault(s => s.Id.Equals(currentSourceId, StringComparison.OrdinalIgnoreCase));
            var currentName = currentSource?.Name ?? string.Empty;

            int currentIndex = Array.FindIndex(configuredNames, n => n.Equals(currentName, StringComparison.OrdinalIgnoreCase));
            int nextIndex = currentIndex >= 0 ? (currentIndex + 1) % configuredNames.Length : 0;
            return configuredNames[nextIndex];
        }
        catch
        {
            return null;
        }
    }

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

    private async Task CycleSourceAsync()
    {
        if (_cambridgeAudio == null || !_cambridgeAudio.IsConnected) return;

        var namesRaw = _cambridgeOptions.SourceSwitchingNames;
        if (string.IsNullOrWhiteSpace(namesRaw)) return;

        var configuredNames = namesRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (configuredNames.Length == 0) return;

        try
        {
            var sources = _cambridgeAudio.Sources;
            var currentSourceId = _cambridgeAudio.State?.Source ?? string.Empty;

            var currentSource = sources.FirstOrDefault(s =>
                s.Id.Equals(currentSourceId, StringComparison.OrdinalIgnoreCase));
            var currentName = currentSource?.Name ?? string.Empty;

            int currentIndex = Array.FindIndex(configuredNames, n =>
                n.Equals(currentName, StringComparison.OrdinalIgnoreCase));

            int nextIndex = currentIndex >= 0
                ? (currentIndex + 1) % configuredNames.Length
                : 0;

            var targetName = configuredNames[nextIndex];

            var targetSource = sources.FirstOrDefault(s =>
                s.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));

            if (targetSource == null)
            {
                _logger.LogWarning(
                    "Source switching: source '{Name}' not found on device. Available: {Sources}",
                    targetName,
                    string.Join(", ", sources.Select(s => s.Name)));
                return;
            }

            await _cambridgeAudio.SetSourceAsync(targetSource.Id).ConfigureAwait(false);
            _logger.LogInformation(
                "Source switched: {From} → {To}",
                string.IsNullOrEmpty(currentName) ? "(unknown)" : currentName,
                targetName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cycle Cambridge Audio source.");
        }
    }

    internal void OnPowerModeChangedInternal(object? sender, EventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
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

    internal float WindowsToCambridgeVolume(float windowsPercent)
    {
        if (_cambridgeOptions.MaxVolume.HasValue)
        {
            return (float)(windowsPercent / 100.0 * _cambridgeOptions.MaxVolume.Value);
        }
        return windowsPercent;
    }

    internal float CambridgeToWindowsVolume(float cambridgePercent)
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
