using System;
using System.Drawing;
using System.Reflection;
using Xunit;

namespace VolumeAssistant.Tests
{
    public class TrayIconTests
    {
        private static Icon CreateIcon(int size, float percent, bool muted)
        {
            // Load the app assembly and find the TrayIconRenderer.Create method
            var asm = Assembly.Load("VolumeAssistant.App");
            var t = asm.GetType("VolumeAssistant.App.Business.TrayIconRenderer");
            Assert.NotNull(t);

            var m = t.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(m);

            var icon = m.Invoke(null, new object?[] { size, percent, muted }) as Icon;
            Assert.NotNull(icon);
            return icon!;
        }

        [Fact]
        public void Indicator_IsVisible_WhenNotMuted()
        {
            using var icon = CreateIcon(64, 30f, false);
            using var bmp = icon.ToBitmap();

            // Sample the centre pixel where the indicator originates
            var c = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
            Assert.True(c.A > 0, "Indicator (centre) should be non-transparent when not muted");
        }

        [Fact]
        public void Indicator_IsVisible_WhenMuted()
        {
            using var icon = CreateIcon(64, 30f, true);
            using var bmp = icon.ToBitmap();

            var c = bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
            Assert.True(c.A > 0, "Indicator (centre) should be non-transparent when muted");
        }

        [Fact]
        public void Indicator_Position_Changes_WithPercent()
        {
            int size = 64;
            float lenFactor = 0.7f;

            using var iconA = CreateIcon(size, 10f, false);
            using var bmpA = iconA.ToBitmap();

            using var iconB = CreateIcon(size, 90f, false);
            using var bmpB = iconB.ToBitmap();

            float margin = size * 0.10f;
            float dim = size - 2f * margin;
            float cx = size / 2f;
            float cy = size / 2f;
            float r = dim / 2f;
            float indicatorLen = r * 0.52f * lenFactor;

            float AngleFor(float p) => -MathF.PI / 2f + ((Math.Clamp(p, 0f, 100f) - 50f) / 100f) * (3f * MathF.PI / 2f);

            var aAngle = AngleFor(10f);
            var bAngle = AngleFor(90f);

            var ax = (int)Math.Round(cx + indicatorLen * MathF.Cos(aAngle));
            var ay = (int)Math.Round(cy + indicatorLen * MathF.Sin(aAngle));

            var bx = (int)Math.Round(cx + indicatorLen * MathF.Cos(bAngle));
            var by = (int)Math.Round(cy + indicatorLen * MathF.Sin(bAngle));

            // Ensure the sampling points are different and both non-transparent
            Assert.False(ax == bx && ay == by, "Sampling points should differ for different percents");

            var ca = bmpA.GetPixel(Math.Clamp(ax, 0, size - 1), Math.Clamp(ay, 0, size - 1));
            var cb = bmpB.GetPixel(Math.Clamp(bx, 0, size - 1), Math.Clamp(by, 0, size - 1));

            Assert.True(ca.A > 0, "Indicator pixel A should be non-transparent for first icon");
            Assert.True(cb.A > 0, "Indicator pixel B should be non-transparent for second icon");

            // Ensure the two rendered bitmaps are not identical. Allow for the
            // possibility that a sampled pixel may match due to anti-aliasing by
            // scanning for any differing pixel across the images.
            bool anyDifferent = false;
            for (int y = 0; y < size && !anyDifferent; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (bmpA.GetPixel(x, y).ToArgb() != bmpB.GetPixel(x, y).ToArgb())
                    {
                        anyDifferent = true;
                        break;
                    }
                }
            }

            Assert.True(anyDifferent, "Rendered icons for different percents should differ.");
        }
    }
}
