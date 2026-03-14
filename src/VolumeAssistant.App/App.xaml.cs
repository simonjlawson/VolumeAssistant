using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VolumeAssistant.Core;
using VolumeAssistant.Service;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;
using VolumeAssistant.Service.Matter;
using MicrosoftHost = Microsoft.Extensions.Hosting.Host;
using IHost = Microsoft.Extensions.Hosting.IHost;

namespace VolumeAssistant.App;

/// <summary>
/// System-tray WPF application that runs the same volume-sync logic as the
/// Windows Service but with a UI window for connection info, configuration and logs.
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;
    private NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;

    internal IHost? AppHost => _host;
    internal ObservableCollection<string> LogEntries { get; } = new();
    internal ICambridgeAudioClient? CambridgeAudioClient { get; private set; }
    internal IOptions<CambridgeAudioOptions>? CambridgeOptions { get; private set; }

    /// <summary>
    /// Completes once the host has been built, services resolved (including any
    /// SSDP device discovery), and the host has started. The main window awaits
    /// this task so the UI shows loading spinners instead of blocking.
    /// </summary>
    internal Task HostReadyTask { get; private set; } = Task.CompletedTask;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Show the tray icon immediately so the app feels responsive.
        CreateTrayIcon();

        // Build and start the host on a background thread. Cambridge Audio
        // SSDP discovery (DiscoverFirstAsync) can take several seconds; running
        // it off the UI thread prevents blocking the tray icon and main window.
        var uiDispatcher = Dispatcher;
        HostReadyTask = Task.Run(async () =>
        {
            var host = BuildHost();

            // Resolving ICambridgeAudioClient triggers the DI factory which may
            // perform SSDP discovery — kept on the background thread intentionally.
            var cambridgeClient = host.Services.GetService<ICambridgeAudioClient>();
            var cambridgeOptions = host.Services.GetService<IOptions<CambridgeAudioOptions>>();

            await host.StartAsync().ConfigureAwait(false);

            // Marshal the resolved references back to the UI thread.
            await uiDispatcher.InvokeAsync(() =>
            {
                _host = host;
                CambridgeAudioClient = cambridgeClient;
                CambridgeOptions = cambridgeOptions;
            });
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        if (_host != null)
        {
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            _host.StopAsync(cts.Token).GetAwaiter().GetResult();
            _host.Dispose();
        }
        base.OnExit(e);
    }

    private IHost BuildHost()
    {
        var builder = MicrosoftHost.CreateApplicationBuilder();

        // Register UI logger with WPF dispatcher for thread-safe UI updates
        var wpfDispatcher = Dispatcher;
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new UiLoggerProvider(LogEntries, a => wpfDispatcher.BeginInvoke(a)));
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);

        // Audio controller
        builder.Services.AddSingleton<IAudioController, WindowsAudioController>();

        // Matter (disabled by default for tray app)
        builder.Services.Configure<MatterOptions>(builder.Configuration.GetSection(MatterOptions.SectionName));
        builder.Services.AddSingleton<MatterDevice>();
        builder.Services.AddSingleton<MatterServer>();
        builder.Services.AddSingleton<MdnsAdvertiser>();

        // Cambridge Audio
        builder.Services.Configure<CambridgeAudioOptions>(
            builder.Configuration.GetSection(CambridgeAudioOptions.SectionName));

        builder.Services.AddSingleton<ICambridgeAudioClient>(sp => CreateCambridgeClient(sp));

        // Register the background worker
        builder.Services.AddHostedService<AppWorker>();

        return builder.Build();
    }

    private static ICambridgeAudioClient CreateCambridgeClient(IServiceProvider sp)
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
                // GetAwaiter().GetResult() is safe here: this factory is called from
                // Task.Run (no synchronisation context), so no deadlock can occur.
                host = CambridgeAudioDiscovery.DiscoverFirstAsync().GetAwaiter().GetResult();
                if (host == null)
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
            factoryLogger?.LogWarning(ex,
                "Failed to create CambridgeAudioClient; falling back to NullCambridgeAudioClient.");
            return new NullCambridgeAudioClient();
        }
    }

    private void CreateTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateSpeakerIcon(),
            Text = "VolumeAssistant",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open", null, (_, _) => ShowMainWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
    }

    private void ExitApplication()
    {
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        Shutdown();
    }

    /// <summary>
    /// Creates a white outlined speaker icon for the system tray.
    /// </summary>
    private static Icon CreateSpeakerIcon()
    {
        // Try to load the embedded volume.ico resource
        var asm = typeof(App).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("volume.ico", StringComparison.OrdinalIgnoreCase));
        if (resourceName != null)
        {
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream != null)
                return new Icon(stream, new System.Drawing.Size(16, 16));
        }

        return DrawSpeakerIcon();
    }

    /// <summary>
    /// Draws a simple white outlined speaker icon (16×16) using GDI+.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Icon DrawSpeakerIcon()
    {
        using var bmp = new System.Drawing.Bitmap(16, 16,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.Transparent);

        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.5f);
        // Speaker body (trapezoid)
        var body = new System.Drawing.Point[] {
            new(2, 5), new(5, 5), new(9, 2), new(9, 13), new(5, 10), new(2, 10)
        };
        g.DrawPolygon(pen, body);

        // Sound wave arc
        g.DrawArc(pen, 10, 4, 4, 7, -60, 120);

        var hIcon = bmp.GetHicon();
        using var iconFromHandle = Icon.FromHandle(hIcon);
        using var ms = new MemoryStream();
        iconFromHandle.Save(ms);
        ms.Position = 0;
        var result = new Icon(ms);
        DestroyIcon(hIcon);
        return result;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
