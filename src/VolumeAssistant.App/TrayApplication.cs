using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using VolumeAssistant.App.Business;
using VolumeAssistant.Core;
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

#if DEBUG
            // When debugging, automatically open the main window so it's easier to debug UI
            try { ShowMainForm(); } catch { }
#endif

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
        return TrayIconRenderer.Create();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
