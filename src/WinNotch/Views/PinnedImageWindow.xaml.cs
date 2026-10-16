using System.Drawing;
using System.Windows;

namespace WinNotch.Views;

public partial class PinnedImageWindow : Window
{
    private readonly Bitmap _bitmap;

    public PinnedImageWindow(Bitmap bitmap)
    {
        _bitmap = (Bitmap)(bitmap ?? throw new ArgumentNullException(nameof(bitmap))).Clone();
        InitializeComponent();
        PinnedImage.Source = CaptureImageInterop.ToBitmapSource(_bitmap);
        double ratio = (double)_bitmap.Width / _bitmap.Height;
        Width = Math.Clamp(_bitmap.Width, 180, 720);
        Height = Math.Clamp(Width / ratio + 36, 120, 640);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _bitmap.Dispose();
        base.OnClosed(e);
    }
}
