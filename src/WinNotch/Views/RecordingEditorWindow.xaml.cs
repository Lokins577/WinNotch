using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows;
using WinNotch.MediaTools;
using WinNotch.Platform.Windows.MediaTools;

namespace WinNotch.Views;

public partial class RecordingEditorWindow : Window
{
    private readonly RecordingProject _project;
    private readonly MediaExportService _exporter;

    public RecordingEditorWindow(RecordingProject project, MediaExportService exporter)
    {
        _project = project;
        _exporter = exporter;
        InitializeComponent();
        ProjectInfo.Text = $"{project.Width}×{project.Height} · {project.FramesPerSecond} fps · {project.Duration:mm\\:ss} · {project.Frames.Count} frames";
        TrimStartText.Text = "0";
        TrimEndText.Text = project.Duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        ZoomStartText.Text = BlurStartText.Text = "0";
        ZoomEndText.Text = BlurEndText.Text = TrimEndText.Text;
        if (project.Frames.FirstOrDefault() is { } frame && File.Exists(frame.Path))
        {
            using var bitmap = new Bitmap(frame.Path);
            PreviewImage.Source = CaptureImageInterop.ToBitmapSource(bitmap);
        }
    }

    private async void OnExportMp4(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export MP4",
            FileName = $"WinNotch-{DateTime.Now:yyyyMMdd-HHmmss}.mp4",
            Filter = "MP4 video|*.mp4"
        };
        if (dialog.ShowDialog(this) != true) return;
        await ExportAsync(() => _exporter.ExportMp4Async(_project, BuildPlan(), dialog.FileName));
    }

    private async void OnExportGif(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export GIF",
            FileName = $"WinNotch-{DateTime.Now:yyyyMMdd-HHmmss}.gif",
            Filter = "Animated GIF|*.gif"
        };
        if (dialog.ShowDialog(this) != true) return;
        await ExportAsync(() => _exporter.ExportGifAsync(_project, BuildPlan(), dialog.FileName));
    }

    private async Task ExportAsync(Func<Task<string>> export)
    {
        ExportProgress.Visibility = Visibility.Visible;
        try { StatusText.Text = "Exported " + await export(); }
        catch (Exception exception) { StatusText.Text = exception.Message; }
        finally { ExportProgress.Visibility = Visibility.Collapsed; }
    }

    private RecordingEditPlan BuildPlan()
    {
        var plan = new RecordingEditPlan
        {
            TrimStart = Seconds(TrimStartText.Text),
            TrimEnd = Seconds(TrimEndText.Text),
            Cuts = ParseRanges(CutsText.Text),
            Watermark = WatermarkText.Text
        };
        if (ZoomCheck.IsChecked == true)
            plan.Zooms.Add(new ZoomEffect(
                new MediaTimeRange(Seconds(ZoomStartText.Text), Seconds(ZoomEndText.Text)),
                Number(ZoomFactorText.Text, 1.5)));
        if (BlurCheck.IsChecked == true)
        {
            double[] values = BlurRectText.Text.Split(',').Select(value => Number(value, 0)).ToArray();
            if (values.Length != 4) throw new FormatException("Blur rectangle needs four comma-separated values.");
            plan.Blurs.Add(new BlurEffect(
                new MediaTimeRange(Seconds(BlurStartText.Text), Seconds(BlurEndText.Text)),
                values[0], values[1], values[2], values[3]));
        }
        plan.Normalize(_project.Duration);
        return plan;
    }

    private static List<MediaTimeRange> ParseRanges(string text)
    {
        var result = new List<MediaTimeRange>();
        foreach (string token in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = token.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length != 2) throw new FormatException($"Invalid cut range: {token}");
            result.Add(new MediaTimeRange(Seconds(parts[0]), Seconds(parts[1])));
        }
        return result;
    }

    private static TimeSpan Seconds(string value) => TimeSpan.FromSeconds(Number(value, 0));
    private static double Number(string value, double fallback)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            ? Math.Max(0, result)
            : fallback;

    private void OnOpenFolder(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo(_project.Directory) { UseShellExecute = true });
}
