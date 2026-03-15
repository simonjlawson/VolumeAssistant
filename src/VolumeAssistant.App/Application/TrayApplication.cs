using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
        private ISourcePopupFactory? _sourcePopupFactory;
        private AppOptions? _appOptions;
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

            // Retrieve app options and popup factory from DI
            _appOptions = _host.Services.GetService<IOptions<AppOptions>>()?.Value;
            _sourcePopupFactory = _host.Services.GetService<ISourcePopupFactory>();

            CreateTrayIcon();
            // Subscribe to log entries to show a transient popup when the Cambridge Audio source is switched
            LogEntries.CollectionChanged += OnLogEntriesChangedForPopup;
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

            // Application options and popup factory
            builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.SectionName));
            builder.Services.AddSingleton<ISourcePopupFactory, DefaultSourcePopupFactory>();

        // Background worker
        builder.Services.AddHostedService<AppWorker>();

        return builder.Build();
    }

    private void CreateTrayIcon()
    {
        var audioController = _host?.Services.GetService<IAudioController>();
        var initialPercent = audioController?.GetVolumePercent() ?? 50f;
        var initialMuted = audioController?.GetMuted() ?? false;
        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconHelper.CreateSpeakerIcon(initialPercent, initialMuted),
            Text = "VolumeAssistant",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open", null, (_, _) => ShowMainForm());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (_, _) => ShowMainForm();

        // Subscribe to Windows volume changes to update the tray icon indicator
        try
        {
            var audio = _host?.Services.GetService<IAudioController>();
            if (audio != null)
            {
                audio.VolumeChanged += (s, e) =>
                {
                    try
                    {
                        var newIcon = TrayIconHelper.CreateSpeakerIcon(e.VolumePercent, e.IsMuted);
                        var old = _notifyIcon?.Icon;
                        _notifyIcon!.Icon = newIcon;
                        old?.Dispose();
                    }
                    catch
                    {
                        // don't let icon update failures crash the app
                    }
                };
            }
        }
        catch
        {
            // best-effort only
        }
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

    private void OnLogEntriesChangedForPopup(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null) return;

        // Look at the newest added entries for a source-switch message
        foreach (var item in e.NewItems)
        {
            if (item is not string s) continue;
            // Respond to either a completed switch or an immediate request so the UI
            // can show a transient popup right away when the media key is pressed.
            if (!s.Contains("Source switched:", StringComparison.OrdinalIgnoreCase)
                && !s.Contains("Source switch requested:", StringComparison.OrdinalIgnoreCase)) continue;

            // Attempt to extract the destination/source name from the log message
            var marker = s.Contains("Source switched:", StringComparison.OrdinalIgnoreCase)
                ? "Source switched:"
                : "Source switch requested:";
            var mpos = s.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            string payload = mpos >= 0 ? s.Substring(mpos + marker.Length).Trim() : s;

            // Expected payload: "{From} → {To}" - take the part after the arrow if present
            string display = payload;
            var arrowIdx = payload.IndexOf('→');
            if (arrowIdx >= 0 && arrowIdx + 1 < payload.Length)
                display = payload.Substring(arrowIdx + 1).Trim();

            // Create and show popup on UI thread (if enabled)
            try
            {
                if (_appOptions?.UseSourcePopup ?? true)
                {
                    var factory = _sourcePopupFactory ?? _host?.Services.GetService<ISourcePopupFactory>() ?? new DefaultSourcePopupFactory();
                    var popup = factory.Create(display);
                    popup.ShowTemporary();
                }
            }
            catch
            {
                // ignore any UI errors for robustness
            }
        }
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
    internal static Icon CreateSpeakerIcon(float indicatorPercent = 50f, bool muted = false)
    {
        return TrayIconRenderer.Create(16, indicatorPercent, muted);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
