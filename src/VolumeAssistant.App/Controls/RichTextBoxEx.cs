using System;
using System.Windows.Forms;

namespace VolumeAssistant.App.Controls;

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
