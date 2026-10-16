using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WinNotch.WindowManagement;

public sealed class DwmThumbnailHost : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const uint DwmTnpRectDestination = 0x00000001;
    private const uint DwmTnpOpacity = 0x00000004;
    private const uint DwmTnpVisible = 0x00000008;
    private const uint DwmTnpSourceClientAreaOnly = 0x00000010;

    private IntPtr _hostHandle;
    private IntPtr _thumbnail;
    private long _sourceHandle;

    public long SourceHandle
    {
        get => _sourceHandle;
        set
        {
            if (_sourceHandle == value) return;
            _sourceHandle = value;
            RegisterThumbnail();
        }
    }

    public bool PreviewAvailable => _thumbnail != IntPtr.Zero;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hostHandle = CreateWindowEx(
            0, "STATIC", string.Empty, WsChild | WsVisible,
            0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        RegisterThumbnail();
        return new HandleRef(this, _hostHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        UnregisterThumbnail();
        if (hwnd.Handle != IntPtr.Zero) DestroyWindow(hwnd.Handle);
        _hostHandle = IntPtr.Zero;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        UpdateThumbnail();
    }

    private void RegisterThumbnail()
    {
        UnregisterThumbnail();
        var source = new IntPtr(_sourceHandle);
        if (_hostHandle == IntPtr.Zero || source == IntPtr.Zero || !IsWindow(source)) return;
        if (DwmRegisterThumbnail(_hostHandle, source, out _thumbnail) < 0)
            _thumbnail = IntPtr.Zero;
        UpdateThumbnail();
    }

    private void UpdateThumbnail()
    {
        if (_thumbnail == IntPtr.Zero || !GetClientRect(_hostHandle, out var client)) return;
        int availableWidth = Math.Max(1, client.Right - client.Left);
        int availableHeight = Math.Max(1, client.Bottom - client.Top);
        int width = availableWidth;
        int height = availableHeight;

        if (DwmQueryThumbnailSourceSize(_thumbnail, out var source) >= 0
            && source.Width > 0 && source.Height > 0)
        {
            double scale = Math.Min(
                (double)availableWidth / source.Width,
                (double)availableHeight / source.Height);
            width = Math.Max(1, (int)Math.Round(source.Width * scale));
            height = Math.Max(1, (int)Math.Round(source.Height * scale));
        }

        int left = (availableWidth - width) / 2;
        int top = (availableHeight - height) / 2;
        var properties = new DwmThumbnailProperties
        {
            Flags = DwmTnpRectDestination | DwmTnpOpacity | DwmTnpVisible | DwmTnpSourceClientAreaOnly,
            Destination = new NativeRect(left, top, left + width, top + height),
            Opacity = 255,
            Visible = 1,
            SourceClientAreaOnly = 0
        };
        DwmUpdateThumbnailProperties(_thumbnail, ref properties);
    }

    private void UnregisterThumbnail()
    {
        if (_thumbnail == IntPtr.Zero) return;
        DwmUnregisterThumbnail(_thumbnail);
        _thumbnail = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public NativeRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width; public int Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmThumbnailProperties
    {
        public uint Flags;
        public NativeRect Destination;
        public NativeRect Source;
        public byte Opacity;
        public int Visible;
        public int SourceClientAreaOnly;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int extendedStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu,
        IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmRegisterThumbnail(
        IntPtr destination, IntPtr source, out IntPtr thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);

    [DllImport("dwmapi.dll")]
    private static extern int DwmUpdateThumbnailProperties(
        IntPtr thumbnail, ref DwmThumbnailProperties properties);

    [DllImport("dwmapi.dll")]
    private static extern int DwmQueryThumbnailSourceSize(
        IntPtr thumbnail, out NativeSize size);
}
