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
/// 4. Matter server operation
/// 5. mDNS advertisement
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly IAudioController _audioController;
    private readonly ICambridgeAudioClient? _cambridgeAudio;
    private readonly MatterDevice _matterDevice;
    private readonly MatterServer _matterServer;
    private readonly MdnsAdvertiser _mdnsAdvertiser;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IAudioController audioController,
        MatterDevice matterDevice,
        MatterServer matterServer,
        MdnsAdvertiser mdnsAdvertiser,
        ILogger<Worker> logger,
        ICambridgeAudioClient? cambridgeAudio = null)
    {
        _audioController = audioController;
        _matterDevice = matterDevice;
        _matterServer = matterServer;
        _mdnsAdvertiser = mdnsAdvertiser;
        _logger = logger;
        _cambridgeAudio = cambridgeAudio;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VolumeAssistant service starting.");

        // Initialize device state from current Windows volume
        float initialVolume = _audioController.GetVolumePercent();
        bool initialMuted = _audioController.GetMuted();
        _matterDevice.UpdateFromVolume(initialVolume, initialMuted);

        _logger.LogInformation(
            "Initial volume: {Volume:F1}%, Muted: {Muted}",
            initialVolume, initialMuted);

        // Subscribe to Windows volume change events
        _audioController.VolumeChanged += OnWindowsVolumeChanged;

        // Subscribe to Matter device state changes (from Matter controller commands)
        _matterDevice.DeviceStateChanged += OnMatterDeviceStateChanged;

        // Start the Matter UDP server
        _matterServer.Start(stoppingToken);

        // Start mDNS advertisement
        _mdnsAdvertiser.Start();

        _logger.LogInformation(
            "VolumeAssistant Matter device ready. Instance: {InstanceName}, Discriminator: {Discriminator}",
            _matterDevice.InstanceName, _matterDevice.Discriminator);

        // Start Cambridge Audio integration if configured
        if (_cambridgeAudio != null)
        {
            _cambridgeAudio.StateChanged += OnCambridgeAudioStateChanged;
            _cambridgeAudio.ConnectionChanged += OnCambridgeAudioConnectionChanged;

            // ConnectAsync runs the reconnect loop in the background
            _ = Task.Run(() => _cambridgeAudio.ConnectAsync(stoppingToken), stoppingToken);

            _logger.LogInformation("Cambridge Audio integration enabled.");
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
        _matterDevice.DeviceStateChanged -= OnMatterDeviceStateChanged;

        if (_cambridgeAudio != null)
        {
            _cambridgeAudio.StateChanged -= OnCambridgeAudioStateChanged;
            _cambridgeAudio.ConnectionChanged -= OnCambridgeAudioConnectionChanged;
            await _cambridgeAudio.DisconnectAsync();
        }

        _mdnsAdvertiser.Stop();
        _matterServer.Stop();

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
            await _matterServer.NotifySubscribersAsync(1, ClusterId.LevelControl, 0x0000, matterLevel);
            await _matterServer.NotifySubscribersAsync(1, ClusterId.OnOff, 0x0000, !e.IsMuted);

            // Sync to Cambridge Audio amplifier
            if (_cambridgeAudio != null && _cambridgeAudio.IsConnected)
            {
                try
                {
                    int volumeInt = (int)Math.Round(e.VolumePercent);
                    await _cambridgeAudio.SetVolumeAsync(volumeInt);
                    await _cambridgeAudio.SetMuteAsync(e.IsMuted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync Windows volume to Cambridge Audio device.");
                }
            }
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

        try
        {
            _audioController.SetVolumePercent(volumePercent);
            _audioController.SetMuted(muted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply volume change from Matter command.");
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
            try
            {
                _audioController.SetVolumePercent(state.VolumePercent.Value);
                _audioController.SetMuted(state.Mute);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply Cambridge Audio volume change to Windows.");
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
}
