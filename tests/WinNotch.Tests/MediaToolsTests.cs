using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using WinNotch.MediaTools;
using WinNotch.Models;
using WinNotch.Platform.Windows.Capture;
using WinNotch.Platform.Windows.MediaTools;
using WinNotch.Services;
using WinNotch.Settings;
using WinNotch.Views;
using Xunit;
using NAudio.Wave;

namespace WinNotch.Tests;

public sealed class MediaToolsTests
{
    [Fact]
    public void Timeline_AppliesTrimAndMergedCutsDeterministically()
    {
        var project = Project("unused", 10, 10, 10);
        project.Duration = TimeSpan.FromSeconds(10);
        project.Frames = Enumerable.Range(0, 11)
            .Select(index => new RecordingFrame(index, TimeSpan.FromSeconds(index), $"{index}.png"))
            .ToList();
        var plan = new RecordingEditPlan
        {
            TrimStart = TimeSpan.FromSeconds(1),
            TrimEnd = TimeSpan.FromSeconds(9),
            Cuts =
            [
                new MediaTimeRange(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5)),
                new MediaTimeRange(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6))
            ]
        };

        Assert.Equal([1, 2, 6, 7, 8, 9], RecordingTimeline.SelectFrames(project, plan).Select(frame => frame.Index));
        Assert.Equal(2, RecordingTimeline.GetKeptRanges(project.Duration, plan).Count);
    }

    [Fact]
    public async Task Recorder_ProducesLocalFrameProjectWithInjectedPixels()
    {
        string root = TempRoot();
        try
        {
            using var recorder = new ScreenRecordingService(
                new WindowsCaptureService(),
                root,
                bounds =>
                {
                    var bitmap = new Bitmap(bounds.Width, bounds.Height);
                    using var graphics = Graphics.FromImage(bitmap);
                    graphics.Clear(Color.DarkSlateBlue);
                    return bitmap;
                });
            var preferences = new RecordingPreferences
            {
                FramesPerSecond = 4,
                CaptureSystemAudio = false,
                CaptureMicrophone = false
            };

            recorder.Start(new Rectangle(0, 0, 64, 48), preferences);
            await Task.Delay(650);
            var project = await recorder.StopAsync();

            Assert.True(project.Frames.Count >= 2);
            Assert.All(project.Frames, frame => Assert.True(File.Exists(frame.Path)));
            Assert.True(File.Exists(Path.Combine(project.Directory, "project.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Exporter_CreatesGifAndMp4FromEditedFrames()
    {
        string root = TempRoot();
        try
        {
            var project = CreateFrameProject(root);
            project.SystemAudioPath = CreateSilentWave(root, "system.wav", project.Duration);
            project.MicrophonePath = CreateSilentWave(root, "microphone.wav", project.Duration);
            var plan = new RecordingEditPlan
            {
                Watermark = "WinNotch",
                Zooms = [new ZoomEffect(new MediaTimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), 1.2)],
                Blurs = [new BlurEffect(new MediaTimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), 0.1, 0.1, 0.2, 0.2)]
            };
            var exporter = new MediaExportService(new WindowsCaptureService());
            string gif = await exporter.ExportGifAsync(project, plan, Path.Combine(root, "result.gif"));
            string mp4 = await exporter.ExportMp4Async(project, plan, Path.Combine(root, "result.mp4"));

            Assert.True(new FileInfo(gif).Length > 0);
            Assert.True(new FileInfo(mp4).Length > 0);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ImageTransform_ResizesConvertsAndWatermarks()
    {
        string root = TempRoot();
        try
        {
            string input = Path.Combine(root, "input.png");
            string output = Path.Combine(root, "output.jpg");
            using (var image = new Bitmap(200, 100)) image.Save(input, ImageFormat.Png);
            var exporter = new MediaExportService(new WindowsCaptureService());

            exporter.TransformImage(input, output, MediaExportFormat.Jpeg,
                new ImageTransformOptions(80, 70, "test"));

            using var result = new Bitmap(output);
            Assert.Equal(80, result.Width);
            Assert.Equal(40, result.Height);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MediaToolWindows_BuildOnStaThread()
    {
        Exception? failure = null;
        string root = TempRoot();
        var project = CreateFrameProject(root);
        var thread = new Thread(() =>
        {
            try
            {
                using var service = new MediaToolsFeatureService();
                var store = new MemoryStore();
                var tool = new RecordingToolWindow(service, new AppSettings(), store);
                var editor = new RecordingEditorWindow(project, service.Exporter);
                var images = new ImageToolsWindow(service.Exporter);
                Assert.NotNull(tool.Content);
                Assert.NotNull(editor.Content);
                Assert.NotNull(images.Content);
                tool.Close(); editor.Close(); images.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Media tools UI test timed out.");
        try { Assert.Null(failure); }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static RecordingProject CreateFrameProject(string root)
    {
        string directory = Path.Combine(root, "project");
        Directory.CreateDirectory(directory);
        var project = Project(directory, 64, 48, 5);
        project.Duration = TimeSpan.FromSeconds(0.6);
        for (int index = 0; index < 3; index++)
        {
            string path = Path.Combine(directory, $"{index}.png");
            using var bitmap = new Bitmap(64, 48);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(index switch { 0 => Color.Red, 1 => Color.Green, _ => Color.Blue });
            bitmap.Save(path, ImageFormat.Png);
            project.Frames.Add(new RecordingFrame(index, TimeSpan.FromSeconds(index * 0.2), path));
        }
        return project;
    }

    private static string CreateSilentWave(string root, string name, TimeSpan duration)
    {
        string path = Path.Combine(root, name);
        var format = new WaveFormat(44_100, 16, 1);
        using var writer = new WaveFileWriter(path, format);
        var silence = new byte[(int)(duration.TotalSeconds * format.AverageBytesPerSecond)];
        writer.Write(silence, 0, silence.Length);
        return path;
    }

    private static RecordingProject Project(string directory, int width, int height, int fps) => new()
    {
        Directory = directory,
        StartedAt = DateTimeOffset.UtcNow,
        Width = width,
        Height = height,
        FramesPerSecond = fps
    };

    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "winnotch-media-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class MemoryStore : IAppSettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
        public void ApplyStartOnBoot(bool enabled) { }
    }
}
