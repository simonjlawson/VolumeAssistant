using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using VolumeAssistant.App.Controls;

namespace VolumeAssistant.App.UI;

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

    [DllImport("user32.dll")]
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
            var sb = new StringBuilder();
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
                using var doc = JsonDocument.Parse(text);
            }
            catch (JsonException jex)
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
