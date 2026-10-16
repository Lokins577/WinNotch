using System.Text.Json.Serialization;

namespace WinNotch.MediaTools;

public sealed class RecordingPreferences
{
    public int FramesPerSecond { get; set; } = 10;
    public int MaximumMinutes { get; set; } = 30;
    public bool CaptureSystemAudio { get; set; } = true;
    public bool CaptureMicrophone { get; set; }
    public int FrameJpegQuality { get; set; } = 85;

    public void Normalize()
    {
        FramesPerSecond = Math.Clamp(FramesPerSecond, 2, 30);
        MaximumMinutes = Math.Clamp(MaximumMinutes, 1, 180);
        FrameJpegQuality = Math.Clamp(FrameJpegQuality, 50, 95);
    }
}

public sealed record RecordingFrame(int Index, TimeSpan Timestamp, string Path);

public sealed class RecordingProject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Directory { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public TimeSpan Duration { get; set; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int FramesPerSecond { get; init; }
    public List<RecordingFrame> Frames { get; set; } = [];
    public string? SystemAudioPath { get; set; }
    public string? MicrophonePath { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public readonly record struct MediaTimeRange(TimeSpan Start, TimeSpan End)
{
    public bool Contains(TimeSpan value) => value >= Start && value < End;
    public bool IsValid => Start >= TimeSpan.Zero && End > Start;
}

public sealed record ZoomEffect(
    MediaTimeRange Range,
    double Factor = 1.5,
    double CenterX = 0.5,
    double CenterY = 0.5);

public sealed record BlurEffect(
    MediaTimeRange Range,
    double Left,
    double Top,
    double Width,
    double Height,
    int Radius = 12);

public sealed class RecordingEditPlan
{
    public TimeSpan TrimStart { get; set; }
    public TimeSpan? TrimEnd { get; set; }
    public List<MediaTimeRange> Cuts { get; set; } = [];
    public List<ZoomEffect> Zooms { get; set; } = [];
    public List<BlurEffect> Blurs { get; set; } = [];
    public string? Watermark { get; set; }

    public void Normalize(TimeSpan duration)
    {
        TrimStart = TimeSpan.FromTicks(Math.Clamp(TrimStart.Ticks, 0, duration.Ticks));
        TrimEnd = TimeSpan.FromTicks(Math.Clamp((TrimEnd ?? duration).Ticks, TrimStart.Ticks, duration.Ticks));
        Cuts = Cuts.Where(range => range.IsValid)
            .Select(range => new MediaTimeRange(
                TimeSpan.FromTicks(Math.Clamp(range.Start.Ticks, TrimStart.Ticks, TrimEnd.Value.Ticks)),
                TimeSpan.FromTicks(Math.Clamp(range.End.Ticks, TrimStart.Ticks, TrimEnd.Value.Ticks))))
            .Where(range => range.IsValid)
            .OrderBy(range => range.Start)
            .ToList();
        Watermark = string.IsNullOrWhiteSpace(Watermark) ? null : Watermark.Trim();
    }
}

public sealed record ImageTransformOptions(
    int? MaximumDimension,
    int Quality,
    string? Watermark);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MediaExportFormat
{
    Mp4,
    Gif,
    Png,
    Jpeg
}
