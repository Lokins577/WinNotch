using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WinNotch.Helpers;

public sealed class DisplayEnergyHotkey : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const int HotKeyId = 0x574E4445;
    private HwndSource? _source;
    public event Action? Pressed;
    public void Register()
    {
        if (_source != null) return;
        _source = new HwndSource(new HwndSourceParameters("WinNotch.DisplayEnergyHotkey") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(Procedure);
        RegisterHotKey(_source.Handle, HotKeyId, 0x0001 | 0x0002 | 0x4000, 0x45);
    }
    private IntPtr Procedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == HotKeyId) { handled = true; Pressed?.Invoke(); }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (_source == null) return;
        UnregisterHotKey(_source.Handle, HotKeyId);
        _source.RemoveHook(Procedure);
        _source.Dispose();
        _source = null;
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
