using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;

namespace VolumeAssistant.App;

/// <summary>
/// Main window showing connection information, configuration and log output.
/// Implemented as a Windows Forms <see cref="Form"/> so that the application is
/// compatible with Native AOT publish.
/// </summary>
internal sealed class MainForm : Form
{
    // ── Dependencies ─────────────────────────────────────────────────────────
    private readonly ICambridgeAudioClient? _cambridgeClient;
    private readonly IAudioController? _audioController;
    private readonly ObservableCollection<string> _logEntries;

    // ── Theme (use centralized Theme class) ───────────────────────────────────

    // ── Connection tab controls ───────────────────────────────────────────────
    private Label _caStatusText = null!;
    private Label _caHostText = null!;
    private Label _caDeviceText = null!;
    private Label _caZoneText = null!;
    private Label _caVolumeText = null!;
    private Button _caConnectButton = null!;
    private Label _winVolumeText = null!;
    private Label _winMutedText = null!;

    // ── Configuration tab controls ────────────────────────────────────────────
    private CheckBox _enableChk = null!;
    private TextBox _hostTb = null!;
    private TextBox _portTb = null!;
    private TextBox _zoneTb = null!;
    private TextBox _startSourceTb = null!;
    private TextBox _startVolumeTb = null!;
    private TextBox _startOutputTb = null!;
    private CheckBox _startPowerChk = null!;
    private CheckBox _closePowerChk = null!;
    private CheckBox _relativeVolumeChk = null!;
    private TextBox _maxVolumeTb = null!;
    private CheckBox _mediaKeysChk = null!;
    private CheckBox _sourceSwitchingChk = null!;
    private TextBox _sourceNamesTb = null!;
    private CheckBox _runAtStartupChk = null!;
    private Label _appSettingsPathLabel = null!;

    // ── Logs tab controls ─────────────────────────────────────────────────────
    private ListBox _logListBox = null!;

    // ── Timers ────────────────────────────────────────────────────────────────
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public MainForm(TrayApplication app)
    {
        _cambridgeClient = app.CambridgeAudioClient;
        _audioController = app.AppHost?.Services.GetService<IAudioController>();
        _logEntries = app.LogEntries;

        BuildUi(app);

        // Subscribe to Cambridge Audio events
        if (_cambridgeClient is not null)
        {
            _cambridgeClient.StateChanged += OnCambridgeStateChanged;
            _cambridgeClient.ConnectionChanged += OnCambridgeConnectionChanged;
        }

        // Subscribe to log changes
        _logEntries.CollectionChanged += OnLogEntriesChanged;

        // Populate config tab controls from current options
        PopulateConfigTab(app);

        // Start up checkbox
        try { _runAtStartupChk.Checked = IsStartupEnabled(); }
        catch { _runAtStartupChk.Enabled = false; }

        // Initial status refresh
        RefreshConnectionInfo();

        // Polling timer
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _refreshTimer.Tick += (_, _) => RefreshConnectionInfo();
        _refreshTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer.Stop();
        _refreshTimer.Dispose();

        if (_cambridgeClient is not null)
        {
            _cambridgeClient.StateChanged -= OnCambridgeStateChanged;
            _cambridgeClient.ConnectionChanged -= OnCambridgeConnectionChanged;
        }
        _logEntries.CollectionChanged -= OnLogEntriesChanged;

        base.OnFormClosed(e);
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnCambridgeStateChanged(object? sender, CambridgeAudioStateChangedEventArgs e)
        => this.BeginInvokeIfRequired(RefreshConnectionInfo);

    private void OnCambridgeConnectionChanged(object? sender, CambridgeAudioConnectionChangedEventArgs e)
        => this.BeginInvokeIfRequired(RefreshConnectionInfo);

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.BeginInvokeIfRequired(() =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                foreach (string item in e.NewItems)
                    _logListBox.Items.Add(item);
            }
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                _logListBox.Items.Clear();
            }
            else
            {
                // Rebuild from scratch for other change types
                _logListBox.BeginUpdate();
                _logListBox.Items.Clear();
                foreach (var entry in _logEntries)
                    _logListBox.Items.Add(entry);
                _logListBox.EndUpdate();
            }

            // Auto-scroll to the last entry
            if (_logListBox.Items.Count > 0)
                _logListBox.TopIndex = _logListBox.Items.Count - 1;
        });
    }

    // ── Connection info refresh ───────────────────────────────────────────────

    private void RefreshConnectionInfo()
    {
        if (_cambridgeClient is null)
        {
            _caStatusText.Text = "Disabled";
            _caHostText.Text = "—";
            _caDeviceText.Text = "—";
            _caZoneText.Text = "—";
            _caVolumeText.Text = "—";
        }
        else
        {
            var state = _cambridgeClient.State;
            var info = _cambridgeClient.Info;

            _caStatusText.Text = _cambridgeClient.IsConnected ? "Connected ✓" : "Disconnected";
            _caStatusText.ForeColor = _cambridgeClient.IsConnected ? Color.LightGreen : Color.OrangeRed;
            _caHostText.Text = info is not null && !string.IsNullOrEmpty(info.UnitId) ? info.UnitId : "—";
            _caDeviceText.Text = info is not null ? $"{info.Name} ({info.Model})" : "—";
            _caZoneText.Text = state?.Source ?? "—";
            _caVolumeText.Text = state?.VolumePercent is not null ? $"{state.VolumePercent}%" : "—";

            _caConnectButton.Text = _cambridgeClient.IsConnected ? "Disconnect" : "Connect";
            _caConnectButton.Enabled = true;
        }

        try
        {
            if (_audioController is not null)
            {
                _winVolumeText.Text = $"{_audioController.GetVolumePercent():F0}%";
                _winMutedText.Text = _audioController.GetMuted() ? "Yes" : "No";
            }
        }
        catch
        {
            // Ignore if audio controller not available on this platform
        }
    }

    // ── Configuration persistence ─────────────────────────────────────────────

    private void PopulateConfigTab(TrayApplication app)
    {
        _appSettingsPathLabel.Text = FindAppSettingsPath();

        var opts = app.CambridgeOptions?.Value;
        if (opts is null) return;

        _enableChk.Checked = opts.Enable;
        _hostTb.Text = opts.Host ?? string.Empty;
        _portTb.Text = opts.Port.ToString();
        _zoneTb.Text = opts.Zone;
        _startSourceTb.Text = opts.StartSourceName ?? string.Empty;
        _startVolumeTb.Text = opts.StartVolume?.ToString() ?? string.Empty;
        _startOutputTb.Text = opts.StartOutput ?? string.Empty;
        _startPowerChk.Checked = opts.StartPower;
        _closePowerChk.Checked = opts.ClosePower;
        _relativeVolumeChk.Checked = opts.RelativeVolume;
        _maxVolumeTb.Text = opts.MaxVolume?.ToString() ?? string.Empty;
        _mediaKeysChk.Checked = opts.MediaKeysEnabled;
        _sourceSwitchingChk.Checked = opts.SourceSwitchingEnabled;
        _sourceNamesTb.Text = opts.SourceSwitchingNames ?? string.Empty;
    }

    private void SaveConfig_Click(object? sender, EventArgs e)
    {
        var path = FindAppSettingsPath();
        if (path.Contains("(not found)"))
        {
            MessageBox.Show(this,
                "appsettings.json not found in application folder.",
                "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var jsonText = File.ReadAllText(path);
            var root = JsonNode.Parse(jsonText) ?? new JsonObject();
            var caNode = root[CambridgeAudioOptions.SectionName] as JsonObject ?? new JsonObject();

            caNode["Enable"] = _enableChk.Checked;
            caNode["Host"] = _hostTb.Text.Trim();
            if (int.TryParse(_portTb.Text.Trim(), out var port)) caNode["Port"] = port;
            caNode["Zone"] = _zoneTb.Text.Trim();
            caNode["StartSourceName"] = _startSourceTb.Text.Trim();
            if (int.TryParse(_startVolumeTb.Text.Trim(), out var sv)) caNode["StartVolume"] = sv;
            else caNode.Remove("StartVolume");
            caNode["StartOutput"] = _startOutputTb.Text.Trim();
            caNode["StartPower"] = _startPowerChk.Checked;
            caNode["ClosePower"] = _closePowerChk.Checked;
            caNode["RelativeVolume"] = _relativeVolumeChk.Checked;
            if (int.TryParse(_maxVolumeTb.Text.Trim(), out var mv)) caNode["MaxVolume"] = mv;
            else caNode.Remove("MaxVolume");
            caNode["MediaKeysEnabled"] = _mediaKeysChk.Checked;
            caNode["SourceSwitchingEnabled"] = _sourceSwitchingChk.Checked;
            caNode["SourceSwitchingNames"] = _sourceNamesTb.Text.Trim();

            root[CambridgeAudioOptions.SectionName] = caNode;

            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            MessageBox.Show(this,
                "Configuration saved to appsettings.json. Restart the app to apply changes.",
                "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Failed to save configuration: {ex.Message}",
                "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void InitializeComponent()
    {

    }

    private void ReloadConfig_Click(object? sender, EventArgs e)
    {
        _appSettingsPathLabel.Text = FindAppSettingsPath();
    }

    // ── Run-at-startup ────────────────────────────────────────────────────────

    private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "VolumeAssistant";

    private static bool IsStartupEnabled()
    {
        try
        {
            using var rk = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: false);
            var v = rk?.GetValue(AppRegistryName) as string;
            if (string.IsNullOrEmpty(v)) return false;
            var exe = GetExecutablePath();
            return v.IndexOf(Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }

    private static bool TryEnableStartup(out string? error)
    {
        error = null;
        try
        {
            var exe = GetExecutablePath();
            using var rk = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: true)
                           ?? Registry.CurrentUser.CreateSubKey(StartupRegistryKey);
            rk.SetValue(AppRegistryName, $"\"{exe}\"");
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private static bool TryDisableStartup(out string? error)
    {
        error = null;
        try
        {
            using var rk = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: true);
            if (rk is null) return true;
            rk.DeleteValue(AppRegistryName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private static string GetExecutablePath()
    {
        try
        {
            var path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(path)) return path;
        }
        catch { }
        return AppContext.BaseDirectory;
    }

    private static string FindAppSettingsPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        return File.Exists(path) ? path : $"{path} (not found)";
    }

    private async void CaConnectButton_Click(object? sender, EventArgs e)
    {
        if (_cambridgeClient is null)
        {
            MessageBox.Show(this,
                "Cambridge Audio client is not available.",
                "Not Available", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _caConnectButton.Enabled = false;
        try
        {
            if (_cambridgeClient.IsConnected)
                await _cambridgeClient.DisconnectAsync();
            else
                await _cambridgeClient.ConnectAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Connection action failed: {ex.Message}",
                "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            RefreshConnectionInfo();
            _caConnectButton.Enabled = true;
        }
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildUi(TrayApplication app)
    {
        Text = "VolumeAssistant";
        Size = new Size(840, 520);
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Theme.ApplyTo(this);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(16),
            //BackColor = Theme.Background,
            //ForeColor = Theme.Foreground,
            Font = Theme.DefaultFont
        };

        tabs.TabPages.Add(BuildConnectionTab());
        tabs.TabPages.Add(BuildConfigurationTab(app));
        tabs.TabPages.Add(BuildLogsTab());

        var statusBar = new StatusStrip { BackColor = Theme.StatusBar };
        statusBar.Items.Add(new ToolStripStatusLabel("Running") { ForeColor = Color.FromArgb(170, 170, 170) });

        Controls.Add(tabs);
        Controls.Add(statusBar);
    }

    private TabPage BuildConnectionTab()
    {
        var page = new TabPage("Connection") { BackColor = Theme.Background, ForeColor = Theme.Foreground };

        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };

        int y = 16;

        void AddSectionHeader(string title)
        {
            panel.Controls.Add(new Label
            {
                Text = title,
                Left = 16,
                Top = y,
                AutoSize = true,
                Font = Theme.HeaderFont,
                ForeColor = Theme.Foreground,
            });
            y += 26;
        }

        (Label label, Label value) AddRow(string labelText)
        {
            var lbl = new Label
            {
                Text = labelText + ":",
                Left = 16,
                Top = y,
                Width = 160,
                AutoSize = false,
                ForeColor = Color.FromArgb(200, 200, 200)
            };
            var val = new Label
            {
                Text = "—",
                Left = 180,
                Top = y,
                Width = 500,
                AutoSize = false,
                ForeColor = Theme.Foreground
            };
            panel.Controls.Add(lbl);
            panel.Controls.Add(val);
            y += 22;
            return (lbl, val);
        }

        // ── Cambridge Audio section ──
        AddSectionHeader("Cambridge Audio");
        (_, _caStatusText) = AddRow("Status");
        (_, _caHostText) = AddRow("Host");
        (_, _caDeviceText) = AddRow("Device");
        (_, _caZoneText) = AddRow("Zone");
        (_, _caVolumeText) = AddRow("Volume");

        _caConnectButton = new Button
        {
            Text = "Connect",
            Left = 16,
            Top = y + 4,
            Width = 90,
            Height = 28,
            BackColor = Theme.Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _caConnectButton.FlatAppearance.BorderColor = Theme.AccentBorder;
        _caConnectButton.Click += CaConnectButton_Click;
        panel.Controls.Add(_caConnectButton);
        y += 40;

        // Separator
        panel.Controls.Add(new Label { Left = 16, Top = y, Width = 700, Height = 1, BackColor = Theme.PanelBorder });
        y += 16;

        // ── Windows Audio section ──
        AddSectionHeader("Windows Audio");
        (_, _winVolumeText) = AddRow("Volume");
        (_, _winMutedText) = AddRow("Muted");

        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildConfigurationTab(TrayApplication app)
    {
        var page = new TabPage("Configuration") { BackColor = Theme.Background, ForeColor = Theme.Foreground };

        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };

        int y = 16;

        panel.Controls.Add(new Label
        {
            Text = "Cambridge Audio Settings",
            Left = 16,
            Top = y,
            AutoSize = true,
            Font = Theme.HeaderFont,
            ForeColor = Theme.Foreground,
        });
        y += 30;

        (Label lbl, Control ctrl) AddConfigRow(string labelText, Control control)
        {
            var label = new Label
            {
                Text = labelText + ":",
                Left = 16,
                Top = y + 2,
                Width = 220,
                AutoSize = false,
                ForeColor = Color.FromArgb(200, 200, 200)
            };
            control.Left = 240;
            control.Top = y;
            control.Width = Math.Max(control.Width, 300);
            control.BackColor = Theme.BackgroundAlt;
            control.ForeColor = Theme.Foreground;
            panel.Controls.Add(label);
            panel.Controls.Add(control);
            y += 26;
            return (label, control);
        }

        _enableChk = new CheckBox { ForeColor = Theme.Foreground, Width = 20 };
        _hostTb = new TextBox();
        _portTb = new TextBox();
        _zoneTb = new TextBox();
        _startSourceTb = new TextBox();
        _startVolumeTb = new TextBox();
        _startOutputTb = new TextBox();
        _startPowerChk = new CheckBox { ForeColor = Theme.Foreground, Width = 20 };
        _closePowerChk = new CheckBox { ForeColor = Theme.Foreground, Width = 20 };
        _relativeVolumeChk = new CheckBox { ForeColor = Theme.Foreground, Width = 20 };
        _maxVolumeTb = new TextBox();
        _mediaKeysChk = new CheckBox { ForeColor = Theme.Foreground, Width = 20 };
        _sourceSwitchingChk = new CheckBox { ForeColor = Theme.Foreground, Width = 20 };
        _sourceNamesTb = new TextBox();

        AddConfigRow("Enable", _enableChk);
        AddConfigRow("Host", _hostTb);
        AddConfigRow("Port", _portTb);
        AddConfigRow("Zone", _zoneTb);
        AddConfigRow("Start Source", _startSourceTb);
        AddConfigRow("Start Volume", _startVolumeTb);
        AddConfigRow("Start Output", _startOutputTb);
        AddConfigRow("Start Power", _startPowerChk);
        AddConfigRow("Close Power", _closePowerChk);
        AddConfigRow("Relative Volume", _relativeVolumeChk);
        AddConfigRow("Max Volume", _maxVolumeTb);
        AddConfigRow("Media Keys", _mediaKeysChk);
        AddConfigRow("Source Switching", _sourceSwitchingChk);
        AddConfigRow("Source Names", _sourceNamesTb);

        // Separator
        panel.Controls.Add(new Label
        {
            Left = 16,
            Top = y,
            Width = 700,
            Height = 1,
            BackColor = Theme.PanelBorder
        });
        y += 12;

        // Run at startup checkbox
        _runAtStartupChk = new CheckBox
        {
            Text = "Run at startup",
            Left = 16,
            Top = y,
            AutoSize = true,
            ForeColor = Theme.Foreground,
            BackColor = Theme.Background,
        };
        _runAtStartupChk.CheckedChanged += (_, _) =>
        {
            if (_runAtStartupChk.Checked)
            {
                if (!TryEnableStartup(out var err))
                {
                    MessageBox.Show(this, $"Failed to add startup entry: {err}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _runAtStartupChk.Checked = false;
                }
            }
            else
            {
                if (!TryDisableStartup(out var err))
                {
                    MessageBox.Show(this, $"Failed to remove startup entry: {err}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _runAtStartupChk.Checked = true;
                }
            }
        };
        panel.Controls.Add(_runAtStartupChk);

        // Reload / Save buttons
        var reloadBtn = new Button
        {
            Text = "Reload",
            Left = 350,
            Top = y,
            Width = 70,
            Height = 26,
            BackColor = Theme.ControlBackground,
            ForeColor = Theme.Foreground,
            FlatStyle = FlatStyle.Flat
        };
        reloadBtn.Click += ReloadConfig_Click;
        panel.Controls.Add(reloadBtn);

        var saveBtn = new Button
        {
            Text = "Save",
            Left = 430,
            Top = y,
            Width = 70,
            Height = 26,
            BackColor = Theme.Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        saveBtn.FlatAppearance.BorderColor = Theme.AccentBorder;
        saveBtn.Click += SaveConfig_Click;
        panel.Controls.Add(saveBtn);

        y += 36;

        // appsettings.json path
        panel.Controls.Add(new Label
        {
            Text = "appsettings.json location",
            Left = 16,
            Top = y,
            AutoSize = true,
            Font = Theme.HeaderFont,
            ForeColor = Theme.Foreground,
        });
        y += 22;

        _appSettingsPathLabel = new Label
        {
            Left = 16,
            Top = y,
            Width = 700,
            Height = 40,
            AutoSize = false,
            ForeColor = Color.FromArgb(170, 170, 170)
        };
        panel.Controls.Add(_appSettingsPathLabel);

        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildLogsTab()
    {
        var page = new TabPage("Logs") { BackColor = Theme.Background, ForeColor = Theme.Foreground };

        _logListBox = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.LogBackground,
            ForeColor = Theme.Foreground,
            Font = new Font("Consolas", 9),
            BorderStyle = BorderStyle.FixedSingle,
            HorizontalScrollbar = true,
        };

        // Populate with any existing entries
        foreach (var entry in _logEntries)
            _logListBox.Items.Add(entry);

        var clearBtn = new Button
        {
            Text = "Clear Logs",
            Dock = DockStyle.Bottom,
            Height = 30,
            BackColor = Theme.ControlBackground,
            ForeColor = Theme.Foreground,
            FlatStyle = FlatStyle.Flat
        };
        clearBtn.Click += (_, _) =>
        {
            // Clearing _logEntries fires CollectionChanged(Reset) which clears _logListBox.Items
            // via OnLogEntriesChanged, so we only need to clear the observable collection here.
            _logEntries.Clear();
        };

        page.Controls.Add(_logListBox);
        page.Controls.Add(clearBtn);
        return page;
    }
}

/// <summary>Extension helper to marshal a delegate to the UI thread via <see cref="Control.BeginInvoke"/>.</summary>
internal static class ControlExtensions
{
    internal static void BeginInvokeIfRequired(this Control control, Action action)
    {
        if (control.InvokeRequired)
            control.BeginInvoke(action);
        else
            action();
    }
}
