using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WinNotch.Helpers;

public sealed class RadialMenuHotkey : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int HotKeyId = 0x574E524D;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkR = 0x52;
    private HwndSource? _source;

    public event Action? Pressed;
    public bool IsRegistered { get; private set; }

    public void Register()
    {
        if (_source != null) return;
        _source = new HwndSource(new HwndSourceParameters("WinNotch.RadialMenuHotkey")
        {
            Width = 0, Height = 0, WindowStyle = 0
        });
        _source.AddHook(WindowProcedure);
        IsRegistered = RegisterHotKey(
            _source.Handle, HotKeyId, ModControl | ModAlt | ModNoRepeat, VkR);
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
        if (IsRegistered) UnregisterHotKey(_source.Handle, HotKeyId);
        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
        _source = null;
        IsRegistered = false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
