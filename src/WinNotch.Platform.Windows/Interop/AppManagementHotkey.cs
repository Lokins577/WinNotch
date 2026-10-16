using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WinNotch.Helpers;

public sealed class AppManagementHotkey : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int HotKeyId = 0x574E504D;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkG = 0x47;
    private HwndSource? _source;
    public event Action? Pressed;

    public void Register()
    {
        if (_source != null) return;
        _source = new HwndSource(new HwndSourceParameters("WinNotch.AppManagementHotkey")
        {
            Width = 0, Height = 0, WindowStyle = 0
        });
        _source.AddHook(WindowProcedure);
        RegisterHotKey(_source.Handle, HotKeyId, ModControl | ModAlt | ModNoRepeat, VkG);
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == HotKeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_source == null) return;
        UnregisterHotKey(_source.Handle, HotKeyId);
        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
        _source = null;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
