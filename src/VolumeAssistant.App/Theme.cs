using System.Drawing;
using System.Windows.Forms;

namespace VolumeAssistant.App;

internal static class Theme
{
    // Refined palette
    public static readonly Color Background = Color.FromArgb(18, 22, 28);       // dark navy
    public static readonly Color BackgroundAlt = Color.FromArgb(28, 34, 42);    // slightly lighter
    public static readonly Color Foreground = Color.FromArgb(230, 230, 230);    // near-white
    public static readonly Color Accent = Color.FromArgb(0, 122, 204);          // pleasant blue
    public static readonly Color AccentBorder = Color.FromArgb(0, 90, 153);     // darker accent

    // Supporting colours
    public static readonly Color PanelBorder = Color.FromArgb(55, 65, 75);
    public static readonly Color StatusBar = Color.FromArgb(15, 18, 22);
    public static readonly Color ControlBackground = Color.FromArgb(36, 42, 50);
    public static readonly Color LogBackground = Color.FromArgb(10, 12, 14);
    public static readonly Color SecondaryForeground = Color.FromArgb(180, 180, 180);
    public static readonly Color MutedForeground = Color.FromArgb(170, 170, 170);

    // Fonts
    public static readonly Font DefaultFont = new Font("Segoe UI", 9F, FontStyle.Regular);
    public static readonly Font HeaderFont = new Font("Segoe UI", 11F, FontStyle.Bold);

    // Apply theme to a top-level form
    public static void ApplyTo(Form form)
    {
        form.SuspendLayout();
        form.BackColor = Background;
        form.ForeColor = Foreground;
        form.Font = DefaultFont;
        form.ResumeLayout();
    }

    // Generic apply to a control and specialised styling for common controls
    public static void ApplyToControl(Control ctrl)
    {
        ctrl.BackColor = Background;
        ctrl.ForeColor = Foreground;
        ctrl.Font = DefaultFont;

        switch (ctrl)
        {
            case Button b:
                StyleButton(b);
                break;
            case TextBox t:
                StyleTextBox(t);
                break;
            case Label l:
                StyleLabel(l);
                break;
            case ListBox lb:
                StyleListBox(lb);
                break;
            case TabPage tp:
                tp.BackColor = Background;
                tp.ForeColor = Foreground;
                break;
            case Panel p:
                p.BackColor = Background;
                break;
        }
    }

    public static void StyleButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = Accent;
        b.ForeColor = Color.White;
        try { b.FlatAppearance.BorderColor = AccentBorder; } catch { }
        b.Font = DefaultFont;
    }

    public static void StyleTextBox(TextBox t)
    {
        t.BackColor = BackgroundAlt;
        t.ForeColor = Foreground;
        t.BorderStyle = BorderStyle.FixedSingle;
        t.Font = DefaultFont;
    }

    public static void StyleLabel(Label l)
    {
        l.ForeColor = Foreground;
        l.Font = DefaultFont;
    }

    public static void StyleListBox(ListBox lb)
    {
        lb.BackColor = LogBackground;
        lb.ForeColor = Foreground;
        lb.Font = new Font("Consolas", 9F);
    }

    // Apply theme to a control tree (recursively)
    public static void ApplyToTree(Control root)
    {
        if (root is null) return;
        ApplyToControl(root);
        foreach (Control child in root.Controls)
            ApplyToTree(child);
    }

    // Style ToolStrip/StatusStrip items which are not Controls
    public static void StyleToolStrip(ToolStrip ts)
    {
        if (ts is null) return;
        try
        {
            ts.BackColor = StatusBar;
            foreach (ToolStripItem item in ts.Items)
            {
                item.ForeColor = MutedForeground;
                if (item is ToolStripLabel lbl) lbl.Font = DefaultFont;
            }
        }
        catch { }
    }
}
