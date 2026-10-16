using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace WinNotch.Platform.Windows.InputEnhancements;

public static class InputContextService
{
    public static string? GetForegroundProcessName()
        => GetProcessName(GetForegroundWindow());

    public static string? GetProcessNameAt(int x, int y)
    {
        IntPtr handle = GetAncestor(WindowFromPoint(new NativePoint(x, y)), 2);
        return GetProcessName(handle);
    }

    public static bool FocusWindowAt(int x, int y)
    {
        IntPtr handle = GetAncestor(WindowFromPoint(new NativePoint(x, y)), 2);
        return handle != IntPtr.Zero && SetForegroundWindow(handle);
    }

    public static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public static bool IsFocusedPasswordField()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            return focused != null
                   && focused.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty) is true;
        }
        catch { return true; }
    }

    public static char? TranslateKey(int virtualKey, int scanCode)
    {
        if (virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C) return null;
        var state = new byte[256];
        if (!GetKeyboardState(state)) return null;
        state[virtualKey & 0xFF] |= 0x80;
        var text = new StringBuilder(4);
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        IntPtr layout = GetKeyboardLayout(foregroundThread);
        int length = ToUnicodeEx(
            (uint)virtualKey, (uint)scanCode, state, text, text.Capacity, 0, layout);
        if (length < 0)
        {
            ToUnicodeEx((uint)virtualKey, (uint)scanCode, state, text, text.Capacity, 0, layout);
            return null;
        }
        return length == 1 ? text[0] : null;
    }

    private static string? GetProcessName(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return null;
        GetWindowThreadProcessId(handle, out uint id);
        try { return Process.GetProcessById((int)id).ProcessName; }
        catch { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public NativePoint(int x, int y) { X = x; Y = y; }
        public int X { get; }
        public int Y { get; }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(
        uint virtualKey, uint scanCode, byte[] state, StringBuilder buffer,
        int bufferSize, uint flags, IntPtr keyboardLayout);
    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);
}
