using VolumeAssistant.Service;
using VolumeAssistant.Service.Audio;
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
builder.Services.AddSingleton<MatterDevice>();
builder.Services.AddSingleton<MatterServer>();
builder.Services.AddSingleton<MdnsAdvertiser>();

// Register the main background service
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
