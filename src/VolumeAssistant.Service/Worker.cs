using Microsoft.Extensions.Options;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;
using VolumeAssistant.Service.Matter;

namespace VolumeAssistant.Service;

/// <summary>
/// Slim wrapper BackgroundService. The heavy lifting is delegated to
/// VolumeSyncCoordinator to keep the worker easy to read and test.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly VolumeSyncCoordinator _coordinator;
    private readonly ILogger<Worker> _logger;
    // Internal test seam: expose the coordinator so tests can inject a
    // CambridgeAudioSyncer directly into the coordinator.
    internal VolumeSyncCoordinator Coordinator => _coordinator;

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
        _logger = logger;

        _coordinator = new VolumeSyncCoordinator(
            audioController,
            matterDevice,
            matterServer,
            mdnsAdvertiser,
            logger,
            cambridgeAudio,
            cambridgeOptions?.Value ?? new CambridgeAudioOptions(),
            matterOptions?.Value ?? new MatterOptions());
    }

    private float WindowsToCambridgeVolume(float windowsPercent)
    {
        return _coordinator.WindowsToCambridgeVolume(windowsPercent);
    }

    private float CambridgeToWindowsVolume(float cambridgePercent)
    {
        return _coordinator.CambridgeToWindowsVolume(cambridgePercent);
    }

    private void OnPowerModeChangedInternal(object? sender, EventArgs e)
    {
        _coordinator.OnPowerModeChangedInternal(sender, e);
    }

    // Backwards-compatibility wrappers for tests that reflect on Worker
    // to exercise media key handlers. These forward to the internal
    // VolumeSyncCoordinator methods via reflection.
    private void OnMediaKeySourceSwitchRequested(object? sender, EventArgs e)
    {
        var m = typeof(VolumeSyncCoordinator).GetMethod(
            "OnMediaKeySourceSwitchRequested",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        m?.Invoke(_coordinator, new object?[] { sender, e });
    }

    private void OnMediaKeyNextTrack(object? sender, EventArgs e)
    {
        var m = typeof(VolumeSyncCoordinator).GetMethod(
            "OnMediaKeyNextTrack",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        m?.Invoke(_coordinator, new object?[] { sender, e });
    }

    private void OnMediaKeyPlayPause(object? sender, EventArgs e)
    {
        var m = typeof(VolumeSyncCoordinator).GetMethod(
            "OnMediaKeyPlayPause",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        m?.Invoke(_coordinator, new object?[] { sender, e });
    }

    private void OnMediaKeyPreviousTrack(object? sender, EventArgs e)
    {
        var m = typeof(VolumeSyncCoordinator).GetMethod(
            "OnMediaKeyPreviousTrack",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        m?.Invoke(_coordinator, new object?[] { sender, e });
    }

    // Backwards-compatibility wrappers for other handlers used in tests
    private void OnWindowsVolumeChanged(object? sender, VolumeChangedEventArgs e)
    {
        _coordinator.OnWindowsVolumeChanged(sender, e);
    }

    private void OnCambridgeAudioStateChanged(object? sender, CambridgeAudioStateChangedEventArgs e)
    {
        _coordinator.OnCambridgeAudioStateChanged(sender, e);
    }

    private void OnMatterDeviceStateChanged(object? sender, (byte Level, bool IsOn) state)
    {
        _coordinator.OnMatterDeviceStateChanged(sender, state);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VolumeAssistant service starting.");

        await _coordinator.StartAsync(stoppingToken).ConfigureAwait(false);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }

        await _coordinator.StopAsync().ConfigureAwait(false);

        _logger.LogInformation("VolumeAssistant service stopped.");
    }
}
