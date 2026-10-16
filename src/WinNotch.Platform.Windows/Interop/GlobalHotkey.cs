using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WinNotch.Helpers;

/// <summary>
/// Registers a global hotkey (Ctrl+Alt+N) to toggle the notch.
/// </summary>
public class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;

    // Modifiers
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _hwnd;
    private HwndSource? _source;

    public event Action? HotkeyPressed;

    public void Register(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        // Ctrl+Alt+N  (N = 0x4E)
        RegisterHotKey(_hwnd, HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x4E);
    }

    public void Unregister()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _source?.RemoveHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
    }
}
