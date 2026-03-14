using Microsoft.Extensions.Options;
using VolumeAssistant.Service;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;
using VolumeAssistant.Service.Matter;

var builder = Host.CreateApplicationBuilder(args);

// Windows Service support – runs as a Windows Service when not in interactive mode
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "VolumeAssistant";
});

// Register audio controller (Windows WASAPI) using a factory so we can
// fall back to a NullAudioController when WASAPI is unavailable (e.g.,
// running as a Windows Service in session 0 where no interactive audio
// endpoint exists).
// Register a retrying audio controller which will attempt to construct
// the WindowsAudioController repeatedly until successful. This avoids a
// permanent fallback to a null implementation while not throwing at
// startup when WASAPI is temporarily unavailable.
builder.Services.AddSingleton<IAudioController>(sp =>
{
    var logger = sp.GetService<ILogger<VolumeAssistant.Service.Audio.RetryingAudioController>>();
    return new VolumeAssistant.Service.Audio.RetryingAudioController(logger);
});

// Register Matter device, server, and mDNS advertiser
// Configure Matter options and register services conditionally based on configuration
builder.Services.Configure<MatterOptions>(builder.Configuration.GetSection("VolumeAssistant:Matter"));
// Always register Matter services so Worker can be constructed even when
// Matter functionality is disabled at runtime. Worker and other components
// should check the configured options before starting servers or advertising.
builder.Services.AddSingleton<MatterDevice>();
builder.Services.AddSingleton<MatterServer>();
builder.Services.AddSingleton<MdnsAdvertiser>();

// Register Cambridge Audio integration (optional – only active when CambridgeAudio:Enable is true)
builder.Services.Configure<CambridgeAudioOptions>(
    builder.Configuration.GetSection(CambridgeAudioOptions.SectionName));

builder.Services.AddSingleton<ICambridgeAudioClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<CambridgeAudioOptions>>().Value;
    if (!opts.IsEnabled)
        return new NullCambridgeAudioClient();

    var factoryLogger = sp.GetService<ILoggerFactory>()?.CreateLogger("Program");

    // Resolve the host: use configured value or fall back to SSDP discovery
    string? host = opts.Host;
    if (string.IsNullOrWhiteSpace(host))
    {
        factoryLogger?.LogInformation(
            "CambridgeAudio:Enable is true but no Host is configured — attempting SSDP device discovery…");
        try
        {
            host = CambridgeAudioDiscovery.DiscoverFirstAsync().GetAwaiter().GetResult();
            if (host == null)
            {
                factoryLogger?.LogWarning(
                    "No Cambridge Audio StreamMagic device was found on the network. " +
                    "Falling back to NullCambridgeAudioClient.");
                return new NullCambridgeAudioClient();
            }

            factoryLogger?.LogInformation("Discovered Cambridge Audio device at {Host}", host);
        }
        catch (Exception ex)
        {
            factoryLogger?.LogWarning(ex,
                "SSDP discovery failed. Falling back to NullCambridgeAudioClient.");
            return new NullCambridgeAudioClient();
        }
    }

    // Build effective options (potentially with discovered host)
    var effectiveOptions = string.Equals(host, opts.Host, StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<IOptions<CambridgeAudioOptions>>()
        : Microsoft.Extensions.Options.Options.Create(new CambridgeAudioOptions
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
        // If the Cambridge client fails to construct, log and fall back to a null implementation
        factoryLogger?.LogWarning(ex,
            "Failed to create CambridgeAudioClient at startup; falling back to NullCambridgeAudioClient.");
        return new NullCambridgeAudioClient();
    }
});

// Register the main background service
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
try
{
    host.Run();
}
catch (Exception ex)
{
    // If the host fails to start, attempt to log the exception. If logging is not available, write to standard error for debugging via console
    try
    {
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Program");
        logger.LogCritical(ex, "Host terminated unexpectedly during startup.");
    }
    catch
    {
        Console.Error.WriteLine($"Host terminated unexpectedly: {ex}");
    }

    throw;
}
