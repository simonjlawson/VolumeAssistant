using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;
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

        // Build editable controls for key CambridgeAudio settings
        ConfigGrid.RowDefinitions.Clear();
        ConfigGrid.Children.Clear();

        int r = 0;

        void AddRow(string label, UIElement control)
        {
            ConfigGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var labelBlock = new System.Windows.Controls.Label
            {
                Content = label + ":",
                Foreground = System.Windows.Media.Brushes.Silver,
            };
            Grid.SetRow(labelBlock, r);
            Grid.SetColumn(labelBlock, 0);
            ConfigGrid.Children.Add(labelBlock);

            Grid.SetRow(control, r);
            Grid.SetColumn(control, 1);
            ConfigGrid.Children.Add(control);
            r++;
        }

        var enableChk = new System.Windows.Controls.CheckBox { IsChecked = opts.Enable, Foreground = System.Windows.Media.Brushes.White };
        AddRow("Enable", enableChk);

        var hostTb = new System.Windows.Controls.TextBox { Text = opts.Host ?? string.Empty };
        AddRow("Host", hostTb);

        var portTb = new System.Windows.Controls.TextBox { Text = opts.Port.ToString() };
        AddRow("Port", portTb);

        var zoneTb = new System.Windows.Controls.TextBox { Text = opts.Zone };
        AddRow("Zone", zoneTb);

        var startSourceTb = new System.Windows.Controls.TextBox { Text = opts.StartSourceName ?? string.Empty };
        AddRow("Start Source", startSourceTb);

        var startVolumeTb = new System.Windows.Controls.TextBox { Text = opts.StartVolume?.ToString() ?? string.Empty };
        AddRow("Start Volume", startVolumeTb);

        var startOutputTb = new System.Windows.Controls.TextBox { Text = opts.StartOutput ?? string.Empty };
        AddRow("Start Output", startOutputTb);

        var startPowerChk = new System.Windows.Controls.CheckBox { IsChecked = opts.StartPower, Foreground = System.Windows.Media.Brushes.White };
        AddRow("Start Power", startPowerChk);

        var closePowerChk = new System.Windows.Controls.CheckBox { IsChecked = opts.ClosePower, Foreground = System.Windows.Media.Brushes.White };
        AddRow("Close Power", closePowerChk);

        var relativeVolumeChk = new System.Windows.Controls.CheckBox { IsChecked = opts.RelativeVolume, Foreground = System.Windows.Media.Brushes.White };
        AddRow("Relative Volume", relativeVolumeChk);

        var maxVolumeTb = new System.Windows.Controls.TextBox { Text = opts.MaxVolume?.ToString() ?? string.Empty };
        AddRow("Max Volume", maxVolumeTb);

        var mediaKeysChk = new System.Windows.Controls.CheckBox { IsChecked = opts.MediaKeysEnabled, Foreground = System.Windows.Media.Brushes.White };
        AddRow("Media Keys", mediaKeysChk);

        var sourceSwitchingChk = new System.Windows.Controls.CheckBox { IsChecked = opts.SourceSwitchingEnabled, Foreground = System.Windows.Media.Brushes.White };
        AddRow("Source Switching", sourceSwitchingChk);

        var sourceNamesTb = new System.Windows.Controls.TextBox { Text = opts.SourceSwitchingNames ?? string.Empty };
        AddRow("Source Names", sourceNamesTb);

        // Store controls on the grid Tag for later save
        ConfigGrid.Tag = new Dictionary<string, object>
        {
            ["Enable"] = enableChk,
            ["Host"] = hostTb,
            ["Port"] = portTb,
            ["Zone"] = zoneTb,
            ["StartSource"] = startSourceTb,
            ["StartVolume"] = startVolumeTb,
            ["StartOutput"] = startOutputTb,
            ["StartPower"] = startPowerChk,
            ["ClosePower"] = closePowerChk,
            ["RelativeVolume"] = relativeVolumeChk,
            ["MaxVolume"] = maxVolumeTb,
            ["MediaKeys"] = mediaKeysChk,
            ["SourceSwitching"] = sourceSwitchingChk,
            ["SourceNames"] = sourceNamesTb,
        };
    }

    private void ReloadConfig_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        PopulateConfigTab(app);
    }

    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        var path = FindAppSettingsPath();
        if (path.Contains("(not found)"))
        {
            System.Windows.MessageBox.Show(this, "appsettings.json not found in application folder.", "Save Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!(ConfigGrid.Tag is Dictionary<string, object> dict))
        {
            System.Windows.MessageBox.Show(this, "Configuration controls not available.", "Save Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var jsonText = File.ReadAllText(path);
            var root = JsonNode.Parse(jsonText) ?? new JsonObject();

            var caNode = root[CambridgeAudioOptions.SectionName] as JsonObject ?? new JsonObject();

            caNode["Enable"] = ((System.Windows.Controls.CheckBox)dict["Enable"]).IsChecked == true;
            caNode["Host"] = ((System.Windows.Controls.TextBox)dict["Host"]).Text.Trim();
            if (int.TryParse(((System.Windows.Controls.TextBox)dict["Port"]).Text.Trim(), out var port)) caNode["Port"] = port;
            caNode["Zone"] = ((System.Windows.Controls.TextBox)dict["Zone"]).Text.Trim();
            caNode["StartSourceName"] = ((System.Windows.Controls.TextBox)dict["StartSource"]).Text.Trim();

            var sv = ((System.Windows.Controls.TextBox)dict["StartVolume"]).Text.Trim();
            if (int.TryParse(sv, out var svv)) caNode["StartVolume"] = svv; else caNode.Remove("StartVolume");

            caNode["StartOutput"] = ((System.Windows.Controls.TextBox)dict["StartOutput"]).Text.Trim();
            caNode["StartPower"] = ((System.Windows.Controls.CheckBox)dict["StartPower"]).IsChecked == true;
            caNode["ClosePower"] = ((System.Windows.Controls.CheckBox)dict["ClosePower"]).IsChecked == true;
            caNode["RelativeVolume"] = ((System.Windows.Controls.CheckBox)dict["RelativeVolume"]).IsChecked == true;

            var mv = ((System.Windows.Controls.TextBox)dict["MaxVolume"]).Text.Trim();
            if (int.TryParse(mv, out var mvv)) caNode["MaxVolume"] = mvv; else caNode.Remove("MaxVolume");

            caNode["MediaKeysEnabled"] = ((System.Windows.Controls.CheckBox)dict["MediaKeys"]).IsChecked == true;
            caNode["SourceSwitchingEnabled"] = ((System.Windows.Controls.CheckBox)dict["SourceSwitching"]).IsChecked == true;
            caNode["SourceSwitchingNames"] = ((System.Windows.Controls.TextBox)dict["SourceNames"]).Text.Trim();

            root[CambridgeAudioOptions.SectionName] = caNode;

            var options = new JsonSerializerOptions { WriteIndented = true };
            var newText = root.ToJsonString(options);
            File.WriteAllText(path, newText);

            System.Windows.MessageBox.Show(this, "Configuration saved to appsettings.json. Restart the app to apply changes.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"Failed to save configuration: {ex.Message}", "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
