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

// Register audio controller (Windows WASAPI)
builder.Services.AddSingleton<IAudioController, WindowsAudioController>();

// Register Matter device, server, and mDNS advertiser
// Configure Matter options and register services conditionally based on configuration
builder.Services.Configure<MatterOptions>(builder.Configuration.GetSection("VolumeAssistant:Matter"));
// Always register Matter services so Worker can be constructed even when
// Matter functionality is disabled at runtime. Worker and other components
// should check the configured options before starting servers or advertising.
builder.Services.AddSingleton<MatterDevice>();
builder.Services.AddSingleton<MatterServer>();
builder.Services.AddSingleton<MdnsAdvertiser>();

// Register Cambridge Audio integration (optional – only active when CambridgeAudio:Host is set)
builder.Services.Configure<CambridgeAudioOptions>(
    builder.Configuration.GetSection(CambridgeAudioOptions.SectionName));

builder.Services.AddSingleton<ICambridgeAudioClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<CambridgeAudioOptions>>().Value;
    if (!opts.IsEnabled)
        return new NullCambridgeAudioClient();
    try
    {
        return new CambridgeAudioClient(
            sp.GetRequiredService<IOptions<CambridgeAudioOptions>>(),
            sp.GetRequiredService<ILogger<CambridgeAudioClient>>());
    }
    catch (Exception ex)
    {
        // If the Cambridge client fails to construct, log and fall back to a null implementation
        var factoryLogger = sp.GetService<ILoggerFactory>();
        factoryLogger?.CreateLogger("Program")
            .LogWarning(ex, "Failed to create CambridgeAudioClient at startup; falling back to NullCambridgeAudioClient.");
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
