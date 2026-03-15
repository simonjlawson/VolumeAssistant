using System;
using System.Windows.Forms;

namespace VolumeAssistant.App.Extensions;

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
