
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
    internal static Icon Create(int size = 16, float indicatorPercent = 50f, bool muted = false)
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

            // Indicator: short line from centre. The indicatorPercent parameter controls
            // the position along the knob arc (0..100). 50 is the middle (12 o'clock).
            float indicatorLen = r * 0.52f;
            // Clamp percent and map to an angle. The knob arc spans 270°; center (50%) -> -90°
            var p = Math.Clamp(indicatorPercent, 0f, 100f);
            // Map 0..100 -> angle range centered at -PI/2 spanning 270° (3*PI/2)
            float angleRad = -MathF.PI / 2f + ((p - 50f) / 100f) * (3f * MathF.PI / 2f);
            // If muted, draw the indicator with a dimmer colour so it remains visible
            // but indicates the muted state.
            if (muted)
            {
                using var mutedPen = new Pen(Color.FromArgb(180, Color.LightGray), penWidth * 0.9f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLine(mutedPen,
                    cx, cy,
                    cx + indicatorLen * MathF.Cos(angleRad),
                    cy + indicatorLen * MathF.Sin(angleRad));
            }
            else
            {
                g.DrawLine(pen,
                    cx, cy,
                    cx + indicatorLen * MathF.Cos(angleRad),
                    cy + indicatorLen * MathF.Sin(angleRad));
            }
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
