using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.CambridgeAudio;
using VolumeAssistant.App.Business;

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
    private Label _buildDateText = null!;

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
    private CheckBox _useSourcePopupChk = null!;
    private Button _advancedEditBtn = null!;

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

        // Ensure the window/taskbar icon matches the tray icon rendering
        try
        {
            // Use a larger size for the taskbar (32px) so the icon scales nicely
            this.Icon = TrayIconRenderer.Create(32);
        }
        catch
        {
            // Swallow any platform-specific failures; not critical
        }
        // Subscribe to Cambridge Audio events
        if (_cambridgeClient is not null)
        {
            _cambridgeClient.StateChanged += OnCambridgeStateChanged;
            _cambridgeClient.ConnectionChanged += OnCambridgeConnectionChanged;
        }
        // Subscribe to log changes
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

/// <summary>
/// Simple modal editor for editing appsettings.json as raw text and saving to per-user AppData.
/// Shows line numbers in a read-only margin. Enter inserts a newline and JSON is validated before saving.
/// </summary>
internal sealed class AppSettingsEditorForm : Form
{
    private readonly string _targetPath;
    private readonly RichTextBoxEx _editor = new();
    private readonly RichTextBox _lineNumbers = new();

    // Win32 message to get first visible line in an edit control
    private const int EM_GETFIRSTVISIBLELINE = 0x00CE;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public AppSettingsEditorForm(string targetPath, string initialContent)
    {
        ArgumentNullException.ThrowIfNull(targetPath);
        _targetPath = targetPath;

        Text = "Edit appsettings.json";
        Size = new Size(800, 600);
        StartPosition = FormStartPosition.CenterParent;

        var pathLabel = new Label
        {
            Text = _targetPath,
            Dock = DockStyle.Top,
            Height = 24,
            AutoEllipsis = true,
        };

        // Line numbers box
        _lineNumbers.ReadOnly = true;
        _lineNumbers.Multiline = true;
        _lineNumbers.Width = 60;
        _lineNumbers.Dock = DockStyle.Left;
        _lineNumbers.ScrollBars = RichTextBoxScrollBars.None;
        _lineNumbers.BorderStyle = BorderStyle.None;
        _lineNumbers.BackColor = SystemColors.ControlLight;
        _lineNumbers.Font = new Font("Consolas", 10);

        // Editor
        _editor.Multiline = true;
        _editor.Font = new Font("Consolas", 10);
        _editor.Dock = DockStyle.Fill;
        _editor.ScrollBars = RichTextBoxScrollBars.Both;
        _editor.WordWrap = false;
        _editor.AcceptsTab = true;
        _editor.Text = initialContent ?? string.Empty;

        _editor.TextChanged += (_, _) => UpdateLineNumbers();
        _editor.VScrolled += (_, _) => SyncLineNumberScroll();
        _editor.FontChanged += (_, _) => { _lineNumbers.Font = _editor.Font; UpdateLineNumbers(); };

        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 40 };
        var saveBtn = new Button { Text = "Save", Width = 90, Left = 10, Top = 6 };
        var cancelBtn = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Left = 110, Top = 6 };
        saveBtn.Click += SaveBtn_Click;

        btnPanel.Controls.Add(saveBtn);
        btnPanel.Controls.Add(cancelBtn);

        var container = new Panel { Dock = DockStyle.Fill };
        container.Controls.Add(_editor);
        container.Controls.Add(_lineNumbers);

        Controls.Add(container);
        Controls.Add(pathLabel);
        Controls.Add(btnPanel);

        // Do not set AcceptButton so Enter inside the multiline editor inserts a newline
        // instead of submitting the form. Keep CancelButton for ESC behaviour.
        CancelButton = cancelBtn;

        UpdateLineNumbers();
    }

    private void UpdateLineNumbers()
    {
        try
        {
            int lines = Math.Max(1, _editor.Lines.Length);
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i <= lines; i++)
            {
                sb.Append(i);
                if (i < lines) sb.AppendLine();
            }
            _lineNumbers.Text = sb.ToString();

            // Adjust width to digits
            int digits = lines.ToString().Length;
            var size = TextRenderer.MeasureText(new string('9', digits) + " ", _editor.Font);
            _lineNumbers.Width = Math.Max(40, size.Width + 8);

            SyncLineNumberScroll();
        }
        catch { }
    }

    private void SyncLineNumberScroll()
    {
        try
        {
            int first = SendMessage(_editor.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
            if (first < 0) first = 0;
            if (first >= _lineNumbers.Lines.Length) first = _lineNumbers.Lines.Length - 1;
            if (first < 0) first = 0;

            int idx = _lineNumbers.GetFirstCharIndexFromLine(first);
            if (idx >= 0 && idx <= _lineNumbers.TextLength)
            {
                _lineNumbers.SelectionStart = idx;
                _lineNumbers.SelectionLength = 0;
                _lineNumbers.ScrollToCaret();
            }
        }
        catch { }
    }

    private void SaveBtn_Click(object? sender, EventArgs e)
    {
        try
        {
            var text = _editor.Text ?? string.Empty;

            // Treat empty/whitespace as an empty JSON object
            if (string.IsNullOrWhiteSpace(text))
            {
                text = "{}";
            }

            // Validate JSON before saving
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(text);
            }
            catch (System.Text.Json.JsonException jex)
            {
                MessageBox.Show(this, $"Invalid JSON: {jex.Message}", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var dir = Path.GetDirectoryName(_targetPath) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_targetPath, text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to save configuration: {ex.Message}", "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>
/// RichTextBox that exposes a VScrolled event when the control is scrolled.
/// </summary>
internal sealed class RichTextBoxEx : RichTextBox
{
    public event EventHandler? VScrolled;
    private const int WM_VSCROLL = 0x0115;
    private const int WM_MOUSEWHEEL = 0x020A;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_VSCROLL || m.Msg == WM_MOUSEWHEEL)
        {
            VScrolled?.Invoke(this, EventArgs.Empty);
        }
    }
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

            _caStatusText.Text = _cambridgeClient.IsConnected ? "Connected" : "Disconnected";
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

        // Build timestamp (compile-time embedded as assembly metadata; convert UTC to local for display)
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var buildMeta = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
                               .FirstOrDefault(a => a.Key == "BuildDateUtc");
            if (buildMeta is not null &&
                DateTime.TryParse(buildMeta.Value, null, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                _buildDateText.Text = dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                _buildDateText.Text = "—";
            }
        }
        catch
        {
            _buildDateText.Text = "—";
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

        // App-level options
        try
        {
            var appOpts = app.AppHost?.Services.GetService<Microsoft.Extensions.Options.IOptions<AppOptions>>()?.Value;
            if (appOpts is not null)
            {
                _useSourcePopupChk.Checked = appOpts.UseSourcePopup;
            }
        }
        catch
        {
            // ignore; optional feature
        }
    }

    private void SaveConfig_Click(object? sender, EventArgs e)
    {
        // Always save edited configuration to per-user AppData so changes are
        // preserved across application upgrades. When loading, we prefer AppData
        // if present (FindAppSettingsPath handles that). For saving, read the
        // existing configuration from AppData if present, else from the
        // application folder, else start a new JSON document.
        var appDataPath = GetAppDataSettingsPath();
        var sourcePath = FindAppSettingsPath();

        // Determine a readable source JSON to preserve other settings
        string? readablePath = null;
        if (!sourcePath.Contains("(not found)") && File.Exists(sourcePath))
            readablePath = sourcePath;
        else if (File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json")))
            readablePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        try
        {
            JsonNode root;
            if (readablePath != null)
            {
                var jsonText = File.ReadAllText(readablePath);
                root = JsonNode.Parse(jsonText) ?? new JsonObject();
            }
            else
            {
                root = new JsonObject();
            }
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

            // App-level section
            var appNode = root[AppOptions.SectionName] as JsonObject ?? new JsonObject();
            appNode["UseSourcePopup"] = _useSourcePopupChk.Checked;
            root[AppOptions.SectionName] = appNode;

            // Ensure AppData folder exists
            var targetDir = Path.GetDirectoryName(appDataPath) ?? string.Empty;
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            File.WriteAllText(appDataPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            _appSettingsPathLabel.Text = appDataPath;
            MessageBox.Show(this,
                "Configuration saved to your user AppData (appsettings.json). Restart the app to apply changes.",
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

    // Build timestamp is provided at compile time via generated BuildInfo.g.cs

    private static string FindAppSettingsPath()
    {
        // Prefer per-user AppData appsettings if present
        var appData = GetAppDataSettingsPath();
        if (File.Exists(appData)) return appData;

        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        return File.Exists(path) ? path : $"{appData} (not found)";
    }

    private static string GetAppDataSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "VolumeAssistant");
        return Path.Combine(dir, "appsettings.json");
    }

    private void AdvancedEdit_Click(object? sender, EventArgs e)
    {
        // Determine source to load: prefer AppData, then app folder, else create new in AppData
        var appDataPath = GetAppDataSettingsPath();
        var appFolderPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        string initialPathToLoad;
        if (File.Exists(appDataPath)) initialPathToLoad = appDataPath;
        else if (File.Exists(appFolderPath)) initialPathToLoad = appFolderPath;
        else initialPathToLoad = appDataPath; // new file will be saved to AppData

        string initialContent = string.Empty;
        try
        {
            if (File.Exists(initialPathToLoad))
                initialContent = File.ReadAllText(initialPathToLoad);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to read configuration: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var dlg = new AppSettingsEditorForm(appDataPath, initialContent);
        var result = dlg.ShowDialog(this);
        if (result == DialogResult.OK)
        {
            // Refresh displayed path and notify user to restart
            _appSettingsPathLabel.Text = FindAppSettingsPath();
            MessageBox.Show(this, "Configuration saved to AppData. Restart the app to apply changes.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
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
        Size = new Size(768, 640);
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Theme.ApplyTo(this);
        // Use the tray icon rendering as the main window icon so the UI and tray match
        Icon = TrayIconRenderer.Create();

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
        };

        // Redraw when selection changes
        tabs.SelectedIndexChanged += (_, _) => tabs.Invalidate();

        tabs.TabPages.Add(BuildConnectionTab());
        tabs.TabPages.Add(BuildConfigurationTab(app));
        tabs.TabPages.Add(BuildLogsTab());

        // Wrap tabs in a Panel (no border) so the whole tab area is grouped without a visible border
        var group = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
        };
        group.Controls.Add(tabs);

        var statusBar = new StatusStrip();
        statusBar.Items.Add(new ToolStripStatusLabel("Running") { ForeColor = Theme.MutedForeground });

        Controls.Add(group);
        Controls.Add(statusBar);

        // Apply theme to the entire control tree and style non-Control strips
        Theme.ApplyToTree(this);
        Theme.StyleToolStrip(statusBar);
    }

    private TabPage BuildConnectionTab()
    {
        var page = new TabPage("Connection");

        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

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
            };
            var val = new Label
            {
                Text = "—",
                Left = 180,
                Top = y,
                Width = 500,
                AutoSize = false,
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
        };
        _caConnectButton.Click += CaConnectButton_Click;
        panel.Controls.Add(_caConnectButton);
        y += 40;

        // Separator
        panel.Controls.Add(new Label { Left = 16, Top = y, Width = 700, Height = 1 });
        y += 16;

        // ── Windows Audio section ──
        AddSectionHeader("Windows Audio");
        (_, _winVolumeText) = AddRow("Volume");
        (_, _winMutedText) = AddRow("Muted");

        // ── App section ──
        AddSectionHeader("App");
        (_, _buildDateText) = AddRow("Build Date");

        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildConfigurationTab(TrayApplication app)
    {
        var page = new TabPage("Configuration");

        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        int y = 16;

        panel.Controls.Add(new Label
        {
            Text = "Cambridge Audio Settings",
            Left = 16,
            Top = y,
            AutoSize = true,
            Font = Theme.HeaderFont,
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
            };
            control.Left = 240;
            control.Top = y;
            control.Width = Math.Max(control.Width, 300);
            panel.Controls.Add(label);
            panel.Controls.Add(control);
            y += 26;
            return (label, control);
        }

        _enableChk = new CheckBox { Width = 20 };
        _hostTb = new TextBox();
        _portTb = new TextBox();
        _zoneTb = new TextBox();
        _startSourceTb = new TextBox();
        _startVolumeTb = new TextBox();
        _startOutputTb = new TextBox();
        _startPowerChk = new CheckBox { Width = 20 };
        _closePowerChk = new CheckBox { Width = 20 };
        _relativeVolumeChk = new CheckBox { Width = 20 };
        _maxVolumeTb = new TextBox();
        _mediaKeysChk = new CheckBox { Width = 20 };
        _sourceSwitchingChk = new CheckBox { Width = 20 };
        _sourceNamesTb = new TextBox();
        _useSourcePopupChk = new CheckBox { Width = 20 };

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
        AddConfigRow("Show Source Popup", _useSourcePopupChk);

        // Separator
        panel.Controls.Add(new Label
        {
            Left = 16,
            Top = y,
            Width = 700,
            Height = 1,
        });
        y += 12;

        // Run at startup checkbox
        _runAtStartupChk = new CheckBox
        {
            Text = "Run at startup",
            Left = 16,
            Top = y,
            AutoSize = true,
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
        };
        saveBtn.Click += SaveConfig_Click;
        panel.Controls.Add(saveBtn);

        var advancedBtn = new Button
        {
            Text = "Advanced Edit",
            Left = 510,
            Top = y,
            Width = 110,
            Height = 26,
        };
        advancedBtn.Click += AdvancedEdit_Click;
        panel.Controls.Add(advancedBtn);
        _advancedEditBtn = advancedBtn;

        y += 36;

        // appsettings.json path
        panel.Controls.Add(new Label
        {
            Text = "appsettings.json location",
            Left = 16,
            Top = y,
            AutoSize = true,
            Font = Theme.HeaderFont,
        });
        y += 22;

        _appSettingsPathLabel = new Label
        {
            Left = 16,
            Top = y,
            Width = 700,
            Height = 40,
            AutoSize = false,
        };
        panel.Controls.Add(_appSettingsPathLabel);

        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildLogsTab()
    {
        var page = new TabPage("Logs");

        _logListBox = new ListBox
        {
            Dock = DockStyle.Fill,
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
