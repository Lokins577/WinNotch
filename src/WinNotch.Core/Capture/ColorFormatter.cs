namespace WinNotch.Capture;

public static class ColorFormatter
{
    public static string ToHsl(CapturedColor color)
    {
        ToNormalized(color, out double red, out double green, out double blue);
        double max = Math.Max(red, Math.Max(green, blue));
        double min = Math.Min(red, Math.Min(green, blue));
        double lightness = (max + min) / 2;
        double saturation = 0;
        double hue = 0;
        if (max != min)
        {
            double delta = max - min;
            saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
            hue = Hue(red, green, blue, max, delta);
        }
        return $"hsl({Math.Round(hue)}, {Math.Round(saturation * 100)}%, {Math.Round(lightness * 100)}%)";
    }

    public static string ToHsv(CapturedColor color)
    {
        ToNormalized(color, out double red, out double green, out double blue);
        double max = Math.Max(red, Math.Max(green, blue));
        double min = Math.Min(red, Math.Min(green, blue));
        double delta = max - min;
        double hue = delta == 0 ? 0 : Hue(red, green, blue, max, delta);
        double saturation = max == 0 ? 0 : delta / max;
        return $"hsv({Math.Round(hue)}, {Math.Round(saturation * 100)}%, {Math.Round(max * 100)}%)";
    }

    private static double Hue(double red, double green, double blue, double max, double delta)
    {
        double value = max == red
            ? (green - blue) / delta + (green < blue ? 6 : 0)
            : max == green
                ? (blue - red) / delta + 2
                : (red - green) / delta + 4;
        return value / 6 * 360;
    }

    private static void ToNormalized(
        CapturedColor color,
        out double red,
        out double green,
        out double blue)
    {
        red = color.Red / 255d;
        green = color.Green / 255d;
        blue = color.Blue / 255d;
    }
}
