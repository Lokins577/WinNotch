using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinNotch.Capture;
using WinNotch.Models;
using WinNotch.Platform.Windows.Capture;
using WinNotch.Settings;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using WpfPoint = System.Windows.Point;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace WinNotch.Views;

public partial class CaptureEditorWindow : Window
{
    private enum EditMode { None, Crop, Redact, Draw, Color }

    private readonly WindowsCaptureService _capture;
    private readonly OfflineImageRecognitionService _recognition;
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _store;
    private Bitmap _bitmap;
    private readonly Bitmap _original;
    private EditMode _mode;
    private WpfPoint _start;
    private WpfPoint _last;
    private bool _dragging;

    public CaptureEditorWindow(
        Bitmap bitmap,
        WindowsCaptureService capture,
        OfflineImageRecognitionService recognition,
        AppSettings settings,
        IAppSettingsStore store)
    {
        _bitmap = (Bitmap)(bitmap ?? throw new ArgumentNullException(nameof(bitmap))).Clone();
        _original = (Bitmap)_bitmap.Clone();
        _capture = capture;
        _recognition = recognition;
        _settings = settings;
        _store = store;
        InitializeComponent();
        RenderBitmap();
        if (_settings.CapturePreferences.CopyAfterCapture) CopyCurrent();
    }

    private void RenderBitmap()
    {
        CaptureImage.Source = CaptureImageInterop.ToBitmapSource(_bitmap);
        CaptureImage.Width = ImageCanvas.Width = _bitmap.Width;
        CaptureImage.Height = ImageCanvas.Height = _bitmap.Height;
    }

    private void OnMode(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string mode } || !Enum.TryParse(mode, out EditMode parsed)) return;
        _mode = parsed;
        StatusText.Text = parsed == EditMode.Color ? "Click a pixel to read its color." : $"{parsed}: drag on the image.";
    }

    private void OnImageMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = _last = Clamp(e.GetPosition(CaptureImage));
        if (_mode == EditMode.Color)
        {
            var color = _bitmap.GetPixel((int)_start.X, (int)_start.Y);
            var captured = new CapturedColor(color.R, color.G, color.B, color.A);
            StatusText.Text = $"{captured.Hex} · {captured.Rgb} · {captured.Hsl} · {captured.Hsv}";
            return;
        }
        _dragging = _mode != EditMode.None;
        CaptureImage.CaptureMouse();
        if (_mode != EditMode.Draw) ShowSelection(_start, _start);
    }

    private void OnImageMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_dragging) return;
        WpfPoint current = Clamp(e.GetPosition(CaptureImage));
        if (_mode == EditMode.Draw)
        {
            using var graphics = Graphics.FromImage(_bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new System.Drawing.Pen(System.Drawing.Color.Red, 4)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(pen, ToDrawing(_last), ToDrawing(current));
            _last = current;
            RenderBitmap();
        }
        else ShowSelection(_start, current);
    }

    private void OnImageMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        CaptureImage.ReleaseMouseCapture();
        WpfPoint end = Clamp(e.GetPosition(CaptureImage));
        var rect = Normalize(_start, end);
        Selection.Visibility = Visibility.Collapsed;
        if (rect.Width < 2 || rect.Height < 2) return;
        if (_mode == EditMode.Crop)
        {
            var next = _bitmap.Clone(rect, PixelFormat.Format32bppArgb);
            _bitmap.Dispose();
            _bitmap = next;
        }
        else if (_mode == EditMode.Redact)
        {
            using var graphics = Graphics.FromImage(_bitmap);
            graphics.FillRectangle(System.Drawing.Brushes.Black, rect);
        }
        RenderBitmap();
    }

    private async void OnOcr(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Running offline OCR…";
            var result = await _recognition.RecognizeTextAsync(_bitmap);
            ShowTextResult($"OCR · {result.Language}", result.Text);
            StatusText.Text = $"OCR found {result.Regions.Count} words.";
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void OnQr(object sender, RoutedEventArgs e)
    {
        var results = _recognition.DecodeQrCodes(_bitmap);
        ShowTextResult("QR results", results.Count == 0 ? "No QR code found." : string.Join("\n\n", results.Select(value => value.Text)));
        StatusText.Text = $"Found {results.Count} QR code{(results.Count == 1 ? string.Empty : "s")}.";
    }

    private void OnCopy(object sender, RoutedEventArgs e) => CopyCurrent();

    private void CopyCurrent()
    {
        System.Windows.Clipboard.SetImage(CaptureImageInterop.ToBitmapSource(_bitmap));
        StatusText.Text = "Copied image to clipboard.";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var preferences = _settings.CapturePreferences;
        string extension = preferences.Format switch
        {
            CaptureImageFormat.Jpeg => ".jpg",
            CaptureImageFormat.Bmp => ".bmp",
            _ => ".png"
        };
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save capture",
            FileName = $"WinNotch-{DateTime.Now:yyyyMMdd-HHmmss}{extension}",
            InitialDirectory = preferences.SaveDirectory,
            Filter = "PNG image|*.png|JPEG image|*.jpg|Bitmap image|*.bmp"
        };
        if (dialog.ShowDialog(this) != true) return;
        preferences.SaveDirectory = Path.GetDirectoryName(dialog.FileName);
        preferences.Format = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => CaptureImageFormat.Jpeg,
            ".bmp" => CaptureImageFormat.Bmp,
            _ => CaptureImageFormat.Png
        };
        preferences.Normalize();
        _store.Save(_settings);
        StatusText.Text = $"Saved {_capture.Save(_bitmap, dialog.FileName, preferences.Format, preferences.JpegQuality)}";
    }

    private void OnPin(object sender, RoutedEventArgs e)
    {
        var window = new PinnedImageWindow(_bitmap);
        window.Show();
        StatusText.Text = "Pinned image above other windows for this session.";
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _bitmap.Dispose();
        _bitmap = (Bitmap)_original.Clone();
        RenderBitmap();
        StatusText.Text = "Restored the original capture.";
    }

    private void ShowSelection(WpfPoint first, WpfPoint second)
    {
        var rect = Normalize(first, second);
        Canvas.SetLeft(Selection, rect.Left);
        Canvas.SetTop(Selection, rect.Top);
        Selection.Width = rect.Width;
        Selection.Height = rect.Height;
        Selection.Visibility = Visibility.Visible;
    }

    private WpfPoint Clamp(WpfPoint point)
        => new(Math.Clamp(point.X, 0, Math.Max(0, _bitmap.Width - 1)), Math.Clamp(point.Y, 0, Math.Max(0, _bitmap.Height - 1)));

    private static DrawingPoint ToDrawing(WpfPoint point) => new((int)Math.Round(point.X), (int)Math.Round(point.Y));
    private static DrawingRectangle Normalize(WpfPoint first, WpfPoint second)
        => DrawingRectangle.FromLTRB(
            (int)Math.Floor(Math.Min(first.X, second.X)),
            (int)Math.Floor(Math.Min(first.Y, second.Y)),
            (int)Math.Ceiling(Math.Max(first.X, second.X)),
            (int)Math.Ceiling(Math.Max(first.Y, second.Y)));

    private void ShowTextResult(string title, string text)
    {
        var box = new WpfTextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(10)
        };
        var window = new Window
        {
            Title = title,
            Width = 620,
            Height = 420,
            Owner = this,
            Content = box,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.Show();
    }

    protected override void OnClosed(EventArgs e)
    {
        _bitmap.Dispose();
        _original.Dispose();
        base.OnClosed(e);
    }
}
