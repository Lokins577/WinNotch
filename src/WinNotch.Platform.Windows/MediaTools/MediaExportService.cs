using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using NAudio.Wave;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using WinNotch.Capture;
using WinNotch.MediaTools;
using WinNotch.Platform.Windows.Capture;
using DrawingColor = System.Drawing.Color;
using DrawingBrushes = System.Drawing.Brushes;

namespace WinNotch.Platform.Windows.MediaTools;

public sealed class MediaExportService
{
    private readonly WindowsCaptureService _imageWriter;

    public MediaExportService(WindowsCaptureService imageWriter)
        => _imageWriter = imageWriter ?? throw new ArgumentNullException(nameof(imageWriter));

    public async Task<string> ExportMp4Async(
        RecordingProject project,
        RecordingEditPlan plan,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareFramesAsync(project, plan, cancellationToken).ConfigureAwait(false);
        try
        {
            var composition = new MediaComposition();
            TimeSpan frameDuration = TimeSpan.FromSeconds(1d / project.FramesPerSecond);
            foreach (string path in prepared.FramePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StorageFile file = await StorageFile.GetFileFromPathAsync(path);
                composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(file, frameDuration));
            }
            foreach (string? audio in new[] { project.SystemAudioPath, project.MicrophonePath })
            {
                if (string.IsNullOrWhiteSpace(audio) || !File.Exists(audio)) continue;
                string trimmed = Path.Combine(prepared.Directory, Path.GetFileName(audio));
                TrimWave(audio, trimmed, RecordingTimeline.GetKeptRanges(project.Duration, plan));
                StorageFile audioFile = await StorageFile.GetFileFromPathAsync(trimmed);
                composition.BackgroundAudioTracks.Add(await BackgroundAudioTrack.CreateFromFileAsync(audioFile));
            }

            string fullPath = Path.GetFullPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            if (!File.Exists(fullPath)) File.WriteAllBytes(fullPath, []);
            StorageFile destination = await StorageFile.GetFileFromPathAsync(fullPath);
            var result = await composition.RenderToFileAsync(
                destination, MediaTrimmingPreference.Precise);
            if (result != TranscodeFailureReason.None)
                throw new InvalidOperationException($"Windows media export failed: {result}.");
            return fullPath;
        }
        finally
        {
            TryDeleteDirectory(prepared.Directory);
        }
    }

    public async Task<string> ExportGifAsync(
        RecordingProject project,
        RecordingEditPlan plan,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var frames = RecordingTimeline.SelectFrames(project, plan);
        if (frames.Count == 0) throw new InvalidOperationException("The edit plan contains no frames.");
        var encoder = new GifBitmapEncoder();
        ushort delay = (ushort)Math.Max(1, Math.Round(100d / project.FramesPerSecond));
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var source = new Bitmap(frame.Path);
            using var processed = ProcessFrame(source, frame.Timestamp, plan);
            using var memory = new MemoryStream();
            processed.Save(memory, ImageFormat.Png);
            memory.Position = 0;
            var image = BitmapFrame.Create(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", delay);
            metadata.SetQuery("/grctlext/Disposal", (byte)2);
            encoder.Frames.Add(BitmapFrame.Create(image, image.Thumbnail, metadata, image.ColorContexts));
        }
        string fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var output = File.Create(fullPath);
        encoder.Save(output);
        return fullPath;
    }

    public string TransformImage(
        string inputPath,
        string outputPath,
        MediaExportFormat format,
        ImageTransformOptions options)
    {
        if (format is not (MediaExportFormat.Png or MediaExportFormat.Jpeg))
            throw new NotSupportedException("Batch image conversion supports PNG and JPEG output.");
        using var source = new Bitmap(inputPath);
        using var transformed = Resize(source, options.MaximumDimension);
        if (!string.IsNullOrWhiteSpace(options.Watermark)) DrawWatermark(transformed, options.Watermark);
        CaptureImageFormat target = format switch
        {
            MediaExportFormat.Jpeg => CaptureImageFormat.Jpeg,
            _ => CaptureImageFormat.Png
        };
        return _imageWriter.Save(transformed, outputPath, target, options.Quality);
    }

    private async Task<PreparedFrames> PrepareFramesAsync(
        RecordingProject project,
        RecordingEditPlan plan,
        CancellationToken cancellationToken)
    {
        var frames = RecordingTimeline.SelectFrames(project, plan);
        if (frames.Count == 0) throw new InvalidOperationException("The edit plan contains no frames.");
        string directory = Path.Combine(project.Directory, "export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var paths = new List<string>(frames.Count);
        await Task.Run(() =>
        {
            for (int index = 0; index < frames.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var source = new Bitmap(frames[index].Path);
                using var processed = ProcessFrame(source, frames[index].Timestamp, plan);
                string path = Path.Combine(directory, $"{index:D7}.png");
                processed.Save(path, ImageFormat.Png);
                paths.Add(path);
            }
        }, cancellationToken).ConfigureAwait(false);
        return new PreparedFrames(directory, paths);
    }

    private static Bitmap ProcessFrame(Bitmap source, TimeSpan timestamp, RecordingEditPlan plan)
    {
        var result = (Bitmap)source.Clone();
        foreach (var zoom in plan.Zooms.Where(value => value.Range.Contains(timestamp)))
        {
            double factor = Math.Clamp(zoom.Factor, 1, 5);
            int width = Math.Max(1, (int)Math.Round(result.Width / factor));
            int height = Math.Max(1, (int)Math.Round(result.Height / factor));
            int left = Math.Clamp((int)Math.Round(zoom.CenterX * result.Width - width / 2d), 0, result.Width - width);
            int top = Math.Clamp((int)Math.Round(zoom.CenterY * result.Height - height / 2d), 0, result.Height - height);
            using var crop = result.Clone(new Rectangle(left, top, width, height), PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(result);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(crop, new Rectangle(0, 0, result.Width, result.Height));
        }
        foreach (var blur in plan.Blurs.Where(value => value.Range.Contains(timestamp))) Pixelate(result, blur);
        if (!string.IsNullOrWhiteSpace(plan.Watermark)) DrawWatermark(result, plan.Watermark);
        return result;
    }

    private static void Pixelate(Bitmap bitmap, BlurEffect effect)
    {
        int left = Math.Clamp((int)Math.Round(effect.Left * bitmap.Width), 0, bitmap.Width - 1);
        int top = Math.Clamp((int)Math.Round(effect.Top * bitmap.Height), 0, bitmap.Height - 1);
        int width = Math.Clamp((int)Math.Round(effect.Width * bitmap.Width), 1, bitmap.Width - left);
        int height = Math.Clamp((int)Math.Round(effect.Height * bitmap.Height), 1, bitmap.Height - top);
        int block = Math.Clamp(effect.Radius, 3, 50);
        using var graphics = Graphics.FromImage(bitmap);
        for (int y = top; y < top + height; y += block)
        for (int x = left; x < left + width; x += block)
        {
            DrawingColor color = bitmap.GetPixel(Math.Min(x + block / 2, left + width - 1), Math.Min(y + block / 2, top + height - 1));
            using var brush = new SolidBrush(color);
            graphics.FillRectangle(brush, x, y, Math.Min(block, left + width - x), Math.Min(block, top + height - y));
        }
    }

    private static void DrawWatermark(Bitmap bitmap, string text)
    {
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float fontSize = Math.Clamp(bitmap.Width / 45f, 12, 36);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold);
        SizeF size = graphics.MeasureString(text, font);
        float x = Math.Max(8, bitmap.Width - size.Width - 14);
        float y = Math.Max(8, bitmap.Height - size.Height - 12);
        using var background = new SolidBrush(DrawingColor.FromArgb(125, 0, 0, 0));
        graphics.FillRectangle(background, x - 5, y - 3, size.Width + 10, size.Height + 6);
        graphics.DrawString(text, font, DrawingBrushes.White, x, y);
    }

    private static Bitmap Resize(Bitmap source, int? maximumDimension)
    {
        if (!maximumDimension.HasValue || maximumDimension <= 0
            || Math.Max(source.Width, source.Height) <= maximumDimension)
            return (Bitmap)source.Clone();
        double scale = (double)maximumDimension.Value / Math.Max(source.Width, source.Height);
        var result = new Bitmap(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));
        using var graphics = Graphics.FromImage(result);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, new Rectangle(0, 0, result.Width, result.Height));
        return result;
    }

    private static void TrimWave(
        string inputPath,
        string outputPath,
        IReadOnlyList<MediaTimeRange> ranges)
    {
        using var reader = new WaveFileReader(inputPath);
        using var writer = new WaveFileWriter(outputPath, reader.WaveFormat);
        byte[] buffer = new byte[64 * 1024];
        foreach (var range in ranges)
        {
            long start = Align((long)(range.Start.TotalSeconds * reader.WaveFormat.AverageBytesPerSecond), reader.WaveFormat.BlockAlign);
            long end = Align((long)(range.End.TotalSeconds * reader.WaveFormat.AverageBytesPerSecond), reader.WaveFormat.BlockAlign);
            reader.Position = Math.Clamp(start, 0, reader.Length);
            long remaining = Math.Max(0, Math.Min(end, reader.Length) - reader.Position);
            while (remaining > 0)
            {
                int read = reader.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read <= 0) break;
                writer.Write(buffer, 0, read);
                remaining -= read;
            }
        }
    }

    private static long Align(long value, int alignment) => value - value % Math.Max(1, alignment);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
        }
        catch { }
    }

    private sealed record PreparedFrames(string Directory, IReadOnlyList<string> FramePaths);
}
