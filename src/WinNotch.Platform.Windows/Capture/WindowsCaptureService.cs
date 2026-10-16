using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using WinNotch.Capture;

namespace WinNotch.Platform.Windows.Capture;

public sealed class WindowsCaptureService
{
    public Rectangle VirtualScreenBounds => System.Windows.Forms.SystemInformation.VirtualScreen;

    public Bitmap CaptureVirtualScreen() => CaptureRegion(VirtualScreenBounds);

    public Bitmap CaptureRegion(Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(region));
        var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    public Task<Bitmap> CaptureWindowAsync(IntPtr handle, CancellationToken cancellationToken = default)
        => Task.Run(() => CaptureWindow(handle), cancellationToken);

    public Bitmap CaptureWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle))
            throw new ArgumentException("The window is unavailable.", nameof(handle));
        if (!TryGetWindowBounds(handle, out var bounds))
            throw new InvalidOperationException("Could not read the window bounds.");

        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        bool rendered;
        using (var graphics = Graphics.FromImage(bitmap))
        {
            IntPtr hdc = graphics.GetHdc();
            try { rendered = PrintWindow(handle, hdc, 0); }
            finally { graphics.ReleaseHdc(hdc); }
        }
        if (rendered) return bitmap;
        bitmap.Dispose();
        return CaptureRegion(bounds);
    }

    public Task<Bitmap> CaptureForegroundWindowAsync(CancellationToken cancellationToken = default)
    {
        IntPtr handle = GetForegroundWindow();
        return CaptureWindowAsync(handle, cancellationToken);
    }

    public CapturedColor SampleColor(Point point)
    {
        using var pixel = CaptureRegion(new Rectangle(point.X, point.Y, 1, 1));
        System.Drawing.Color color = pixel.GetPixel(0, 0);
        return new CapturedColor(color.R, color.G, color.B, color.A);
    }

    public string Save(Bitmap bitmap, string path, CaptureImageFormat format, int jpegQuality = 92)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (format == CaptureImageFormat.Jpeg)
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(value => value.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(jpegQuality, 20, 100));
            bitmap.Save(fullPath, codec, parameters);
        }
        else
        {
            bitmap.Save(fullPath, format == CaptureImageFormat.Bmp ? ImageFormat.Bmp : ImageFormat.Png);
        }
        return fullPath;
    }

    public static bool TryGetWindowBounds(IntPtr handle, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (DwmGetWindowAttribute(handle, 9, out var frame, Marshal.SizeOf<NativeRect>()) != 0
            && !GetWindowRect(handle, out frame)) return false;
        bounds = Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr handle, IntPtr deviceContext, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr handle, uint attribute, out NativeRect value, int size);
}
