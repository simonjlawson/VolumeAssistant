using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.Matter;
using VolumeAssistant.Service.Matter.Clusters;

namespace VolumeAssistant.Service;

/// <summary>
/// Main background worker service that coordinates:
/// 1. Windows audio volume monitoring
/// 2. Matter device state synchronization
/// 3. Matter server operation
/// 4. mDNS advertisement
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly IAudioController _audioController;
    private readonly MatterDevice _matterDevice;
    private readonly MatterServer _matterServer;
    private readonly MdnsAdvertiser _mdnsAdvertiser;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IAudioController audioController,
        MatterDevice matterDevice,
        MatterServer matterServer,
        MdnsAdvertiser mdnsAdvertiser,
        ILogger<Worker> logger)
    {
        _audioController = audioController;
        _matterDevice = matterDevice;
        _matterServer = matterServer;
        _mdnsAdvertiser = mdnsAdvertiser;
        _logger = logger;
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
        _mdnsAdvertiser.Stop();
        _matterServer.Stop();

        _logger.LogInformation("VolumeAssistant service stopped.");
    }

    /// <summary>
    /// Handles Windows volume changes and propagates them to Matter subscribers.
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

        // Notify subscribers asynchronously
        _ = Task.Run(async () =>
        {
            await _matterServer.NotifySubscribersAsync(1, ClusterId.LevelControl, 0x0000, matterLevel);
            await _matterServer.NotifySubscribersAsync(1, ClusterId.OnOff, 0x0000, !e.IsMuted);
        });
    }

    /// <summary>
    /// Handles Matter device state changes (from Matter controller commands)
    /// and applies them to Windows volume.
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
    }
}
