using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace WinNotch.Helpers;

/// <summary>
/// Captures the screen behind the notch and processes it into an iOS-style
/// "liquid glass" backdrop:
///   1. edge-lens refraction — pixels near the rim sample from OUTSIDE the
///      shape, compressed inward, so the edge visibly bends the world behind
///      it (the analytic equivalent of the SVG displacement map used by the
///      LiquidGlassV2 prototype);
///   2. saturation boost (~150%) + slight lift, Apple's "vibrancy";
///   3. GPU BlurEffect on the target Path handles the blur itself.
/// Zero allocation per frame: GDI+ buffer, LUTs, and WriteableBitmap are reused.
/// </summary>
public static class BackdropBlurHelper
{
    // Width of the refractive rim, in physical pixels.
    private const int RimSize = 12;
    // Saturation as a /64 fixed-point factor: 96/64 = 1.5x.
    private const int SaturationNum = 96;
    // Small brightness lift so the glass reads slightly luminous.
    private const int Lift = 5;

    // Reusable GDI+ bitmap — avoids allocation each frame
    private static Bitmap? _captureBuf;
    private static int _bufW, _bufH;

    // Cached dest→source lens LUTs (rebuilt only when the notch size changes)
    private static int[]? _lutX, _lutY;
    private static long _lutXKey = -1, _lutYKey = -1;

    /// <summary>
    /// Captures a padded screen region into a WriteableBitmap, applying edge
    /// refraction and vibrancy. All coordinates are physical pixels.
    /// </summary>
    public static void CaptureInto(WriteableBitmap wb, int x, int y, int width, int height, int padding = 8)
    {
        if (width < 4 || height < 4) return;

        // The rim can't be wider than a third of the smaller side (tiny pills)
        int rim = Math.Min(RimSize, Math.Min(width, height) / 3);

        int cw = width + padding * 2;
        int ch = height + padding * 2;

        // Clamp to the virtual desktop, NOT to 0 — monitors left of the primary
        // have negative coordinates and would otherwise lose the backdrop.
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        int cx = Math.Clamp(x - padding, vs.Left, Math.Max(vs.Left, vs.Right - cw));
        int cy = Math.Clamp(y - padding, vs.Top, Math.Max(vs.Top, vs.Bottom - ch));

        try
        {
            // Reuse or recreate the GDI+ capture buffer
            if (_captureBuf == null || _bufW != cw || _bufH != ch)
            {
                _captureBuf?.Dispose();
                _captureBuf = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
                _bufW = cw;
                _bufH = ch;
            }

            using (var g = Graphics.FromImage(_captureBuf))
            {
                g.CopyFromScreen(cx, cy, 0, 0, new System.Drawing.Size(cw, ch), CopyPixelOperation.SourceCopy);
            }

            EnsureLut(ref _lutX, ref _lutXKey, width, padding, rim);
            EnsureLut(ref _lutY, ref _lutYKey, height, padding, rim);
            var lutX = _lutX!;
            var lutY = _lutY!;

            // Lock source pixels
            var srcData = _captureBuf.LockBits(
                new Rectangle(0, 0, cw, ch),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            wb.Lock();
            try
            {
                unsafe
                {
                    byte* src = (byte*)srcData.Scan0;
                    byte* dst = (byte*)wb.BackBuffer;
                    int srcStride = srcData.Stride;
                    int dstStride = wb.BackBufferStride;

                    for (int dy = 0; dy < height; dy++)
                    {
                        byte* srcRow = src + lutY[dy] * srcStride;
                        byte* dstRow = dst + dy * dstStride;

                        for (int dx = 0; dx < width; dx++)
                        {
                            byte* sp = srcRow + lutX[dx] * 4;
                            int b = sp[0];
                            int gch = sp[1];
                            int r = sp[2];

                            // Vibrancy: boost saturation around luma, slight lift
                            int gray = (r * 77 + gch * 150 + b * 29) >> 8;
                            r = gray + (((r - gray) * SaturationNum) >> 6) + Lift;
                            gch = gray + (((gch - gray) * SaturationNum) >> 6) + Lift;
                            b = gray + (((b - gray) * SaturationNum) >> 6) + Lift;

                            byte* dp = dstRow + dx * 4;
                            dp[0] = (byte)(b < 0 ? 0 : b > 255 ? 255 : b);
                            dp[1] = (byte)(gch < 0 ? 0 : gch > 255 ? 255 : gch);
                            dp[2] = (byte)(r < 0 ? 0 : r > 255 ? 255 : r);
                            dp[3] = 255;
                        }
                    }
                }

                wb.AddDirtyRect(new Int32Rect(0, 0, width, height));
            }
            finally
            {
                wb.Unlock();
            }

            _captureBuf.UnlockBits(srcData);
        }
        catch
        {
            // Silently ignore capture failures
        }
    }

    /// <summary>
    /// Builds the 1D lens mapping for one axis: identity through the body, and a
    /// compressive curve inside the rim that pulls samples from the padded region
    /// OUTSIDE the shape — content bends around the edge like curved glass.
    /// </summary>
    private static void EnsureLut(ref int[]? lut, ref long key, int size, int padding, int rim)
    {
        long k = ((long)size << 24) | ((long)padding << 12) | (uint)rim;
        if (lut != null && key == k) return;

        var table = new int[size];
        int max = size + padding * 2 - 1;

        for (int d = 0; d < size; d++)
        {
            double s;
            if (rim > 0 && d < rim)
            {
                double t = d / (double)rim;
                s = (rim + padding) * Math.Pow(t, 0.68);
            }
            else if (rim > 0 && d >= size - rim)
            {
                double t = (size - 1 - d) / (double)rim;
                s = (size + 2.0 * padding - 1) - (rim + padding) * Math.Pow(t, 0.68);
            }
            else
            {
                s = d + padding;
            }

            table[d] = Math.Clamp((int)Math.Round(s), 0, max);
        }

        lut = table;
        key = k;
    }
}
