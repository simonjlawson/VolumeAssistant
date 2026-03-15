
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace VolumeAssistant.App.Business;

/// <summary>
/// Renders the system-tray icon: a white outline volume dial on a transparent background.
/// </summary>
internal static class TrayIconRenderer
{
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Creates an <see cref="Icon"/> containing a white outline volume dial on a
    /// transparent background.  The native GDI handle is released before returning.
    /// </summary>
    internal static Icon Create(int size = 16)
    {
        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float margin = size * 0.10f;
            float dim = size - 2f * margin;
            float cx = size / 2f;
            float cy = size / 2f;
            float r = dim / 2f;
            float penWidth = Math.Max(1f, size * 0.10f);

            using var pen = new Pen(Color.White, penWidth) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };

            // Volume-knob arc: 135° start, 270° sweep — leaves a gap at the bottom
            g.DrawArc(pen, margin, margin, dim, dim, 135f, 270f);

            // Indicator: short line from centre pointing to 12 o'clock (mid-volume)
            float indicatorLen = r * 0.52f;
            float angleRad = -MathF.PI / 2f; // −90° = straight up
            g.DrawLine(pen,
                cx, cy,
                cx + indicatorLen * MathF.Cos(angleRad),
                cy + indicatorLen * MathF.Sin(angleRad));
        }

        var hIcon = bmp.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }
}
