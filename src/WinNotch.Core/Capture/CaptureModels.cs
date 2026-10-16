using System.Text.Json.Serialization;

namespace WinNotch.Capture;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CaptureImageFormat
{
    Png,
    Jpeg,
    Bmp
}

public sealed class CapturePreferences
{
    public string? SaveDirectory { get; set; }
    public CaptureImageFormat Format { get; set; } = CaptureImageFormat.Png;
    public int JpegQuality { get; set; } = 92;
    public bool CopyAfterCapture { get; set; } = true;

    public void Normalize()
    {
        SaveDirectory = string.IsNullOrWhiteSpace(SaveDirectory) ? null : SaveDirectory.Trim();
        JpegQuality = Math.Clamp(JpegQuality, 20, 100);
    }
}

public readonly record struct CapturedColor(byte Red, byte Green, byte Blue, byte Alpha = 255)
{
    public string Hex => Alpha == 255
        ? $"#{Red:X2}{Green:X2}{Blue:X2}"
        : $"#{Alpha:X2}{Red:X2}{Green:X2}{Blue:X2}";
    public string Rgb => $"rgb({Red}, {Green}, {Blue})";
    public string Hsl => ColorFormatter.ToHsl(this);
    public string Hsv => ColorFormatter.ToHsv(this);
}

public sealed record OcrTextRegion(string Text, int Left, int Top, int Width, int Height);
public sealed record OcrDocument(string Text, IReadOnlyList<OcrTextRegion> Regions, string Language);
public sealed record QrCodeResult(string Text, string Format);
