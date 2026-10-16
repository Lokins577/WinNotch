using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinNotch.Helpers;

public static class ScreenHelper
{
    /// <summary>
    /// Gets the full screen bounds of the primary monitor in WPF device-independent pixels.
    /// Pass the actual window to get an accurate DPI reading.
    /// </summary>
    public static Rect GetPrimaryScreenBounds(Visual visual)
    {
        return GetScreenBounds(Screen.PrimaryScreen!, visual);
    }

    /// <summary>
    /// Converts a monitor's physical-pixel bounds to WPF device-independent pixels.
    /// The app is system-DPI-aware, so one uniform scale maps the whole virtual desktop.
    /// </summary>
    public static Rect GetScreenBounds(Screen screen, Visual visual)
    {
        var dpiScale = GetDpiScale(visual);
        return new Rect(
            screen.Bounds.Left / dpiScale,
            screen.Bounds.Top / dpiScale,
            screen.Bounds.Width / dpiScale,
            screen.Bounds.Height / dpiScale
        );
    }

    /// <summary>Gets the screen containing a physical-pixel point (e.g. Cursor.Position).</summary>
    public static Screen ScreenFromPhysicalPoint(System.Drawing.Point physicalPoint)
    {
        return Screen.FromPoint(physicalPoint);
    }

    /// <summary>
    /// Finds a screen by its device name; falls back to the primary screen when the
    /// device is missing (monitor unplugged) or the name is null.
    /// </summary>
    public static Screen FindScreenByDevice(string? deviceName)
    {
        if (!string.IsNullOrEmpty(deviceName))
        {
            var match = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == deviceName);
            if (match != null)
                return match;
        }
        return Screen.PrimaryScreen!;
    }

    /// <summary>Gets the native monitor handle the window currently sits on.</summary>
    public static IntPtr GetMonitorHandle(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        return hwnd == IntPtr.Zero ? IntPtr.Zero : MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
    }

    /// <summary>
    /// DPI scale of a specific monitor (1.0 = 96 dpi). Under Per-Monitor V2 this
    /// returns each screen's true scale, letting each island size itself for the
    /// monitor it lives on.
    /// </summary>
    public static double GetScaleForScreen(Screen screen)
    {
        try
        {
            var center = new POINT
            {
                x = screen.Bounds.Left + screen.Bounds.Width / 2,
                y = screen.Bounds.Top + screen.Bounds.Height / 2
            };
            var monitor = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                return dpiX / 96.0;
        }
        catch
        {
            // shcore unavailable — fall through to system scale
        }

        using var g = System.Drawing.Graphics.FromHwnd(nint.Zero);
        return g.DpiX / 96.0;
    }

    /// <summary>
    /// Gets the DPI scale factor from a specific visual element.
    /// </summary>
    public static double GetDpiScale(Visual visual)
    {
        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget != null)
            return source.CompositionTarget.TransformToDevice.M11;

        // Fallback: use system DPI
        using var g = System.Drawing.Graphics.FromHwnd(nint.Zero);
        return g.DpiX / 96.0;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x, y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
