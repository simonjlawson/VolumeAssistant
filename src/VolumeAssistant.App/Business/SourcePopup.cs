using System.Drawing;
using System.Windows.Forms;

namespace VolumeAssistant.App.Business;

internal sealed class SourcePopup : Form, ISourcePopup
{
    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _lifetimeTimer;
    private readonly System.Windows.Forms.Timer _fadeTimer;

    public SourcePopup(string text)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        // Use opaque backcolor and rely on whole-window Opacity for translucency
        BackColor = Color.FromArgb(30, 30, 30); // opaque dark background
        Opacity = 0.6; // subtle

        // Rounded label
        _label = new Label
        {
            AutoSize = false,
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 16, FontStyle.Bold),
            Dock = DockStyle.Fill,
        };

        Padding = new Padding(18, 10, 18, 10);
        Width = 340;
        Height = 64;

        Controls.Add(_label);

        // Lifetime timer: how long before starting fade-out
        _lifetimeTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        _lifetimeTimer.Tick += (_, _) => StartFadeOut();

        // Fade timer reduces opacity until close
        _fadeTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _fadeTimer.Tick += FadeStep;
    }

    public void ShowTemporary()
    {
        PositionAboveTaskbar();
        Show();
        _lifetimeTimer.Start();
    }

    private void StartFadeOut()
    {
        _lifetimeTimer.Stop();
        _fadeTimer.Start();
    }

    private void FadeStep(object? sender, EventArgs e)
    {
        if (Opacity <= 0.05)
        {
            _fadeTimer.Stop();
            Close();
            return;
        }
        Opacity -= 0.06;
        // move up slightly while fading for subtle effect
        Top -= 1;
    }

    private void PositionAboveTaskbar()
    {
        var screen = Screen.PrimaryScreen;
        if (screen == null) return;

        var working = screen.WorkingArea; // excludes taskbar
        // place horizontally center, and bottom such that bottom is 32px above taskbar
        int x = working.Left + (working.Width - Width) / 2;
        int y = working.Bottom - 32 - Height; // 32px above taskbar

        Location = new Point(x, Math.Max(16, y));
    }
}
