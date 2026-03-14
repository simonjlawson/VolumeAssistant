using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;

namespace VolumeAssistant.App;

/// <summary>
/// Main window showing connection information, configuration, and log output.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ICambridgeAudioClient? _cambridgeClient;
    private readonly IAudioController? _audioController;
    private readonly ObservableCollection<string> _logEntries;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

    public MainWindow()
    {
        InitializeComponent();

        var app = (App)System.Windows.Application.Current;
        _cambridgeClient = app.CambridgeAudioClient;
        _audioController = null; // resolved below if available
        _logEntries = app.LogEntries;

        // Try to get the audio controller from the host services
        if (app.AppHost != null)
        {
            _audioController = app.AppHost.Services.GetService(typeof(IAudioController)) as IAudioController;
        }

        // Bind log entries to the list box
        LogListBox.ItemsSource = _logEntries;
        _logEntries.CollectionChanged += OnLogEntriesChanged;

        // Subscribe to Cambridge Audio state changes
        if (_cambridgeClient != null)
        {
            _cambridgeClient.StateChanged += OnCambridgeStateChanged;
            _cambridgeClient.ConnectionChanged += OnCambridgeConnectionChanged;
        }

        // Populate config tab
        PopulateConfigTab(app);

        // Initial data refresh
        RefreshConnectionInfo();

        // Poll every 2 seconds to keep audio info current
        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += (_, _) => RefreshConnectionInfo();
        _refreshTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        if (_cambridgeClient != null)
        {
            _cambridgeClient.StateChanged -= OnCambridgeStateChanged;
            _cambridgeClient.ConnectionChanged -= OnCambridgeConnectionChanged;
        }
        _logEntries.CollectionChanged -= OnLogEntriesChanged;
        base.OnClosed(e);
    }

    private void OnCambridgeStateChanged(object? sender, CambridgeAudioStateChangedEventArgs e)
        => Dispatcher.BeginInvoke(RefreshConnectionInfo);

    private void OnCambridgeConnectionChanged(object? sender, CambridgeAudioConnectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(RefreshConnectionInfo);

    private void RefreshConnectionInfo()
    {
        // Cambridge Audio
        if (_cambridgeClient == null)
        {
            CaStatusText.Text = "Disabled";
            CaHostText.Text = "—";
            CaDeviceText.Text = "—";
            CaZoneText.Text = "—";
            CaVolumeText.Text = "—";
        }
        else
        {
            var state = _cambridgeClient.State;
            var info = _cambridgeClient.Info;

            CaStatusText.Text = _cambridgeClient.IsConnected ? "Connected ✓" : "Disconnected";
            CaStatusText.Foreground = _cambridgeClient.IsConnected
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.OrangeRed;

            CaHostText.Text = info != null && !string.IsNullOrEmpty(info.UnitId) ? info.UnitId : "—";
            CaDeviceText.Text = info != null
                ? $"{info.Name} ({info.Model})"
                : "—";
            CaZoneText.Text = state?.Source ?? "—";
            CaVolumeText.Text = state?.VolumePercent != null ? $"{state.VolumePercent}%" : "—";
        }

        // Windows audio
        try
        {
            if (_audioController != null)
            {
                WinVolumeText.Text = $"{_audioController.GetVolumePercent():F0}%";
                WinMutedText.Text = _audioController.GetMuted() ? "Yes" : "No";
            }
        }
        catch
        {
            // ignore if audio controller not available on this platform
        }
    }

    private void PopulateConfigTab(App app)
    {
        AppSettingsPathText.Text = FindAppSettingsPath();

        var opts = app.CambridgeOptions?.Value;
        if (opts == null) return;

        var rows = new (string Label, string Value)[]
        {
            ("Enable", opts.Enable.ToString()),
            ("Host", string.IsNullOrWhiteSpace(opts.Host) ? "(auto-discover)" : opts.Host),
            ("Port", opts.Port.ToString()),
            ("Zone", opts.Zone),
            ("Start Source", opts.StartSourceName ?? "—"),
            ("Start Volume", opts.StartVolume?.ToString() ?? "—"),
            ("Start Output", opts.StartOutput ?? "—"),
            ("Start Power", opts.StartPower.ToString()),
            ("Close Power", opts.ClosePower.ToString()),
            ("Relative Volume", opts.RelativeVolume.ToString()),
            ("Max Volume", opts.MaxVolume?.ToString() ?? "—"),
            ("Media Keys", opts.MediaKeysEnabled.ToString()),
            ("Source Switching", opts.SourceSwitchingEnabled.ToString()),
            ("Source Names", opts.SourceSwitchingNames ?? "—"),
        };

        ConfigGrid.RowDefinitions.Clear();
        ConfigGrid.Children.Clear();

        for (int i = 0; i < rows.Length; i++)
        {
            ConfigGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new System.Windows.Controls.Label
            {
                Content = rows[i].Label + ":",
                Foreground = System.Windows.Media.Brushes.Silver,
            };
            Grid.SetRow(labelBlock, i);
            Grid.SetColumn(labelBlock, 0);
            ConfigGrid.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = rows[i].Value,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(valueBlock, i);
            Grid.SetColumn(valueBlock, 1);
            ConfigGrid.Children.Add(valueBlock);
        }
    }

    private static string FindAppSettingsPath()
    {
        var exeDir = AppContext.BaseDirectory;
        var path = System.IO.Path.Combine(exeDir, "appsettings.json");
        return System.IO.File.Exists(path) ? path : $"{path} (not found)";
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Auto-scroll to bottom when new entries arrive
        if (_logEntries.Count > 0)
            LogListBox.ScrollIntoView(_logEntries[^1]);
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
        => _logEntries.Clear();
}
