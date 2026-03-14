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
    private ICambridgeAudioClient? _cambridgeClient;
    private IAudioController? _audioController;
    private readonly ObservableCollection<string> _logEntries;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

    public MainWindow()
    {
        InitializeComponent();

        var app = (App)System.Windows.Application.Current;
        _logEntries = app.LogEntries;

        // Bind log entries immediately — no async work required for the Logs tab.
        LogListBox.ItemsSource = _logEntries;
        _logEntries.CollectionChanged += OnLogEntriesChanged;

        // Show loading overlays while the host initialises in the background.
        SetLoadingState(true);

        // Create the timer now but start it only after initialisation completes.
        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += (_, _) => RefreshConnectionInfo();

        // Await host initialisation asynchronously once the window is visible so
        // loading spinners are rendered before the await begins.
        Loaded += async (_, _) => await FinishInitializingAsync(app);
    }

    /// <summary>
    /// Awaits <see cref="App.HostReadyTask"/> then wires up services, populates
    /// the UI, hides the loading overlays, and starts the refresh timer.
    /// </summary>
    private async Task FinishInitializingAsync(App app)
    {
        try
        {
            await app.HostReadyTask.ConfigureAwait(true); // resume on UI thread

            // Resolve services now that the host is ready.
            _cambridgeClient = app.CambridgeAudioClient;
            if (app.AppHost != null)
                _audioController = app.AppHost.Services.GetService(typeof(IAudioController)) as IAudioController;

            // Subscribe to Cambridge Audio state changes.
            if (_cambridgeClient != null)
            {
                _cambridgeClient.StateChanged += OnCambridgeStateChanged;
                _cambridgeClient.ConnectionChanged += OnCambridgeConnectionChanged;
            }

            // Populate UI panels now that all data is available.
            PopulateConfigTab(app);
            RefreshConnectionInfo();

            // Hide loading overlays and start the polling timer.
            SetLoadingState(false);
            _refreshTimer.Start();
        }
        catch (Exception ex)
        {
            StatusBarText.Text = $"Initialisation error: {ex.Message}";
            SetLoadingState(false);
        }
    }

    /// <summary>Shows or hides the loading overlay on the Connection and Configuration tabs.</summary>
    private void SetLoadingState(bool isLoading)
    {
        var loading = isLoading ? Visibility.Visible : Visibility.Collapsed;
        var content = isLoading ? Visibility.Collapsed : Visibility.Visible;

        ConnectionLoadingOverlay.Visibility = loading;
        ConnectionContent.Visibility = content;
        ConfigLoadingOverlay.Visibility = loading;
        ConfigContent.Visibility = content;
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
