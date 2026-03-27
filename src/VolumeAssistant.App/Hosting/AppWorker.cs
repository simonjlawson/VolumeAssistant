using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VolumeAssistant.Service;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;
using VolumeAssistant.Service.Matter;

namespace VolumeAssistant.App;

/// <summary>
/// Background service for the tray app. Mirrors the Windows Service Worker,
/// delegating to <see cref="VolumeSyncCoordinator"/> for the actual logic.
/// </summary>
internal sealed class AppWorker : BackgroundService
{
    private readonly VolumeSyncCoordinator _coordinator;
    private readonly ILogger<AppWorker> _logger;

    public AppWorker(
        IAudioController audioController,
        MatterDevice matterDevice,
        MatterServer matterServer,
        MdnsAdvertiser mdnsAdvertiser,
        ILogger<AppWorker> logger,
        ICambridgeAudioClient? cambridgeAudio = null,
        IOptions<CambridgeAudioOptions>? cambridgeOptions = null,
        IOptions<MatterOptions>? matterOptions = null,
        IOptions<AppOptions>? appOptions = null)
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
            matterOptions?.Value ?? new MatterOptions(),
            appOptions?.Value?.BalanceOffset ?? AppOptions.DefaultBalanceOffset,
            appOptions?.Value?.AdjustWindowsBalance ?? false,
            cambridgeOptions?.Value?.AdjustCambridgeAudioBalance ?? true,
            appOptions?.Value?.ApplyBalanceOnStartup ?? false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VolumeAssistant tray app starting.");

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

        _logger.LogInformation("VolumeAssistant tray app stopped.");
    }
}
