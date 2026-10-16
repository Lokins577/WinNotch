using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinNotch.Services;

public static class ColorExtractionService
{
    /// <summary>
    /// Extracts the dominant color from a BitmapImage using a simple
    /// histogram-based approach on a downsampled copy.
    /// </summary>
    public static Color GetDominantColor(BitmapImage source)
    {
        try
        {
            // Downsample to 40x40 for speed
            const int sampleSize = 40;
            var resized = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var scaled = new TransformedBitmap(resized,
                new ScaleTransform(
                    sampleSize / (double)source.PixelWidth,
                    sampleSize / (double)source.PixelHeight));

            int width = scaled.PixelWidth;
            int height = scaled.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            scaled.CopyPixels(pixels, stride, 0);

            long totalR = 0, totalG = 0, totalB = 0;
            int count = 0;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];

                if (a < 128) continue;

                // Skip very dark and very bright pixels (they're not interesting)
                int brightness = (r + g + b) / 3;
                if (brightness < 30 || brightness > 240) continue;

                // Boost saturation weight: prefer colorful pixels
                int max = Math.Max(r, Math.Max(g, b));
                int min = Math.Min(r, Math.Min(g, b));
                int saturation = max - min;
                int weight = Math.Max(1, saturation);

                totalR += r * weight;
                totalG += g * weight;
                totalB += b * weight;
                count += weight;
            }

            if (count == 0)
                return Colors.DimGray;

            return Color.FromRgb(
                (byte)(totalR / count),
                (byte)(totalG / count),
                (byte)(totalB / count));
        }
        catch
        {
            return Colors.DimGray;
        }
    }
}
