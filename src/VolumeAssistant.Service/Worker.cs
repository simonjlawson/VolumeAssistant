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
