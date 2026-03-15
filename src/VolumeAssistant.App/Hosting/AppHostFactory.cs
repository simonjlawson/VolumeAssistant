using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VolumeAssistant.Service.CambridgeAudio;

namespace VolumeAssistant.App;

/// <summary>
/// Factory helpers for services that are used by both the host builder and unit tests.
/// </summary>
internal static class AppHostFactory
{
    /// <summary>
    /// Creates the <see cref="ICambridgeAudioClient"/> for the DI container.
    /// Returns <see cref="NullCambridgeAudioClient"/> when the integration is disabled,
    /// when no host is configured and SSDP discovery finds nothing, or on any error.
    /// </summary>
    internal static ICambridgeAudioClient CreateCambridgeClient(IServiceProvider sp)
    {
        var opts = sp.GetRequiredService<IOptions<CambridgeAudioOptions>>().Value;
        if (!opts.IsEnabled)
            return new NullCambridgeAudioClient();

        var factoryLogger = sp.GetService<ILoggerFactory>()?.CreateLogger("App");

        string? host = opts.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            factoryLogger?.LogInformation(
                "CambridgeAudio:Enable is true but no Host configured — attempting SSDP discovery…");
            try
            {
                host = CambridgeAudioDiscovery.DiscoverFirstAsync().GetAwaiter().GetResult();
                if (host is null)
                {
                    factoryLogger?.LogWarning(
                        "No Cambridge Audio StreamMagic device found. Falling back to NullCambridgeAudioClient.");
                    return new NullCambridgeAudioClient();
                }
                factoryLogger?.LogInformation("Discovered Cambridge Audio device at {Host}", host);
            }
            catch (Exception ex)
            {
                factoryLogger?.LogWarning(ex, "SSDP discovery failed. Falling back to NullCambridgeAudioClient.");
                return new NullCambridgeAudioClient();
            }
        }

        var effectiveOptions = string.Equals(host, opts.Host, StringComparison.OrdinalIgnoreCase)
            ? sp.GetRequiredService<IOptions<CambridgeAudioOptions>>()
            : Options.Create(new CambridgeAudioOptions
            {
                Enable = opts.Enable,
                Host = host,
                Port = opts.Port,
                Zone = opts.Zone,
                InitialReconnectDelayMs = opts.InitialReconnectDelayMs,
                MaxReconnectDelayMs = opts.MaxReconnectDelayMs,
                RequestTimeoutMs = opts.RequestTimeoutMs,
                StartSourceName = opts.StartSourceName,
                StartVolume = opts.StartVolume,
                StartOutput = opts.StartOutput,
                StartPower = opts.StartPower,
                ClosePower = opts.ClosePower,
                RelativeVolume = opts.RelativeVolume,
                MaxVolume = opts.MaxVolume,
            });

        try
        {
            return new CambridgeAudioClient(
                effectiveOptions,
                sp.GetRequiredService<ILogger<CambridgeAudioClient>>());
        }
        catch (Exception ex)
        {
            factoryLogger?.LogWarning(ex,
                "Failed to create CambridgeAudioClient; falling back to NullCambridgeAudioClient.");
            return new NullCambridgeAudioClient();
        }
    }
}
