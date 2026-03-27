using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace VolumeAssistant.App.UI;

/// <summary>
/// Modal dialog that displays the history of previous file copy operations and allows
/// the user to retry any that failed.
/// </summary>
internal sealed class PreviousCopiesDialog : Form
{
    private readonly IReadOnlyList<FileCopyEntry> _entries;
    private readonly ListView _listView;
    private readonly Button _retryBtn;

    public PreviousCopiesDialog(IReadOnlyList<FileCopyEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries;

        Text = "Previous File Copies";
        Size = new Size(900, 480);
        MinimumSize = new Size(600, 300);
        StartPosition = FormStartPosition.CenterParent;

        // ── ListView ────────────────────────────────────────────────────────
        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
        };

        _listView.Columns.Add("Source", 260);
        _listView.Columns.Add("Destination", 260);
        _listView.Columns.Add("Status", 75);
        _listView.Columns.Add("Timestamp", 140);
        _listView.Columns.Add("Error", 150);

        // ── Button panel ────────────────────────────────────────────────────
        var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 44 };

        _retryBtn = new Button
        {
            Text = "Retry Failed Files",
            Width = 130,
            Height = 28,
            Left = 10,
            Top = 8,
        };
        _retryBtn.Click += RetryBtn_Click;

        var closeBtn = new Button
        {
            Text = "Close",
            Width = 90,
            Height = 28,
            Left = 150,
            Top = 8,
            DialogResult = DialogResult.Cancel,
        };

        btnPanel.Controls.Add(_retryBtn);
        btnPanel.Controls.Add(closeBtn);

        Controls.Add(_listView);
        Controls.Add(btnPanel);

        CancelButton = closeBtn;

        // Apply theming
        Theme.ApplyTo(this);
        Theme.ApplyToTree(btnPanel);
        _listView.BackColor = Theme.LogBackground;
        _listView.ForeColor = Theme.Foreground;
        _listView.Font = Theme.DefaultFont;

        PopulateList();
        UpdateRetryButtonState();
    }

    // ── Population / refresh ────────────────────────────────────────────────

    private void PopulateList()
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();

        foreach (var entry in _entries)
            _listView.Items.Add(BuildListViewItem(entry));

        _listView.EndUpdate();
    }

    private static Color StatusColor(FileCopyStatus status) => status switch
    {
        FileCopyStatus.Failed => Color.Firebrick,
        FileCopyStatus.Success => Color.DarkGreen,
        _ => Theme.Foreground,
    };

    private static ListViewItem BuildListViewItem(FileCopyEntry entry)
    {
        var item = new ListViewItem(entry.SourcePath);
        item.SubItems.Add(entry.DestinationPath);
        item.SubItems.Add(entry.Status.ToString());
        item.SubItems.Add(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
        item.SubItems.Add(entry.ErrorMessage ?? string.Empty);
        item.Tag = entry;
        item.ForeColor = StatusColor(entry.Status);
        return item;
    }

    private static void RefreshListViewItem(ListViewItem item, FileCopyEntry entry)
    {
        item.SubItems[2].Text = entry.Status.ToString();
        item.SubItems[3].Text = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        item.SubItems[4].Text = entry.ErrorMessage ?? string.Empty;
        item.ForeColor = StatusColor(entry.Status);
    }

    private void UpdateRetryButtonState()
    {
        _retryBtn.Enabled = _entries.Any(e => e.Status == FileCopyStatus.Failed);
    }

    // ── Retry logic ─────────────────────────────────────────────────────────

    private void RetryBtn_Click(object? sender, EventArgs e)
    {
        var failedItems = _listView.Items
            .Cast<ListViewItem>()
            .Where(item => item.Tag is FileCopyEntry { Status: FileCopyStatus.Failed })
            .ToList();

        if (failedItems.Count == 0)
            return;

        _retryBtn.Enabled = false;

        try
        {
            foreach (var item in failedItems)
            {
                if (item.Tag is not FileCopyEntry entry)
                    continue;

                try
                {
                    var dir = Path.GetDirectoryName(entry.DestinationPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    File.Copy(entry.SourcePath, entry.DestinationPath, overwrite: true);

                    entry.Status = FileCopyStatus.Success;
                    entry.ErrorMessage = null;
                    entry.Timestamp = DateTime.Now;
                }
                catch (Exception ex)
                {
                    entry.Status = FileCopyStatus.Failed;
                    entry.ErrorMessage = ex.Message;
                    entry.Timestamp = DateTime.Now;
                }

                RefreshListViewItem(item, entry);
            }

            _listView.Refresh();
        }
        finally
        {
            UpdateRetryButtonState();
        }
    }
}
