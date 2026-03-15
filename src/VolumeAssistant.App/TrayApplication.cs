using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VolumeAssistant.Core;
using VolumeAssistant.Service;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;
using VolumeAssistant.Service.Matter;
using IHost = Microsoft.Extensions.Hosting.IHost;
using MicrosoftHost = Microsoft.Extensions.Hosting.Host;

namespace VolumeAssistant.App;

/// <summary>
/// Manages the Windows Forms application lifecycle: builds the DI host, shows the system-tray
/// icon and opens/closes the main window.  Replaces the WPF <c>App</c> class.
/// </summary>
internal sealed class TrayApplication : IDisposable
{
    private IHost? _host;
    private NotifyIcon? _notifyIcon;
    private MainForm? _mainForm;

    internal IHost? AppHost => _host;
    internal ObservableCollection<string> LogEntries { get; } = new();
    internal ICambridgeAudioClient? CambridgeAudioClient { get; private set; }
    internal IOptions<CambridgeAudioOptions>? CambridgeOptions { get; private set; }

    /// <summary>Builds the host, creates the tray icon and starts the Windows Forms message loop.</summary>
    public void Run()
    {
        _host = BuildHost();
        CambridgeAudioClient = _host.Services.GetService<ICambridgeAudioClient>();
        CambridgeOptions = _host.Services.GetService<IOptions<CambridgeAudioOptions>>();

        CreateTrayIcon();
        _ = _host.StartAsync();

        Application.Run();
    }

    public void Dispose()
    {
        _notifyIcon?.Dispose();
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        _host?.StopAsync(cts.Token).GetAwaiter().GetResult();
        _host?.Dispose();
    }

    private IHost BuildHost()
    {
        var builder = MicrosoftHost.CreateApplicationBuilder();

        // Marshal log entries onto the UI thread using the Windows Forms synchronisation context.
        var syncContext = System.Threading.SynchronizationContext.Current;
        Action<Action> dispatch = syncContext is not null
            ? a => syncContext.Post(_ => a(), null)
            : a => a();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new UiLoggerProvider(LogEntries, dispatch));
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);

        // Audio controller
        builder.Services.AddSingleton<IAudioController, WindowsAudioController>();

        // Matter
        builder.Services.Configure<MatterOptions>(builder.Configuration.GetSection(MatterOptions.SectionName));
        builder.Services.AddSingleton<MatterDevice>();
        builder.Services.AddSingleton<MatterServer>();
        builder.Services.AddSingleton<MdnsAdvertiser>();

        // Cambridge Audio
        builder.Services.Configure<CambridgeAudioOptions>(
            builder.Configuration.GetSection(CambridgeAudioOptions.SectionName));
        builder.Services.AddSingleton<ICambridgeAudioClient>(sp => AppHostFactory.CreateCambridgeClient(sp));

        // Background worker
        builder.Services.AddHostedService<AppWorker>();

        return builder.Build();
    }

    private void CreateTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconHelper.CreateSpeakerIcon(),
            Text = "VolumeAssistant",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open", null, (_, _) => ShowMainForm());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => ShowMainForm();
    }

    private void ShowMainForm()
    {
        if (_mainForm == null || _mainForm.IsDisposed)
        {
            _mainForm = new MainForm(this);
            _mainForm.FormClosed += (_, _) => _mainForm = null;
        }

        _mainForm.Show();
        _mainForm.Activate();
        if (_mainForm.WindowState == FormWindowState.Minimized)
            _mainForm.WindowState = FormWindowState.Normal;
    }

    private void ExitApplication()
    {
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        Application.Exit();
    }
}

/// <summary>
/// Helper for creating the tray icon, kept separate to allow unit testing without WinForms.
/// </summary>
internal static class TrayIconHelper
{
    /// <summary>
    /// Returns the volume icon: first tries the embedded <c>volume.ico</c> resource,
    /// then falls back to a programmatically drawn speaker glyph.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static Icon CreateSpeakerIcon()
    {
        var asm = typeof(TrayApplication).Assembly;
        var resourceName = Array.Find(
            asm.GetManifestResourceNames(),
            n => n.EndsWith("volume.ico", StringComparison.OrdinalIgnoreCase));

        if (resourceName is not null)
        {
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is not null)
                return new Icon(stream, new Size(16, 16));
        }

        return DrawSpeakerIcon();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Icon DrawSpeakerIcon()
    {
        using var bmp = new System.Drawing.Bitmap(16, 16,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.Transparent);

        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 1.5f);
        // Speaker body (trapezoid)
        g.DrawPolygon(pen, new System.Drawing.Point[] {
            new(2, 5), new(5, 5), new(9, 2), new(9, 13), new(5, 10), new(2, 10)
        });
        // Sound-wave arc
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
