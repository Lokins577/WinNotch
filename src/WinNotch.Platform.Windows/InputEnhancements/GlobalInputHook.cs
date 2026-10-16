using System.Runtime.InteropServices;

namespace WinNotch.Platform.Windows.InputEnhancements;

public sealed record GlobalMouseEvent(
    int Message,
    int X,
    int Y,
    int Data,
    bool Injected);

public sealed record GlobalKeyboardEvent(
    int VirtualKey,
    int ScanCode,
    bool KeyDown,
    bool Injected);

public sealed class GlobalInputHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const uint WmQuit = 0x0012;
    private readonly HookProcedure _mouseProcedure;
    private readonly HookProcedure _keyboardProcedure;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private readonly ManualResetEventSlim _started = new();

    public GlobalInputHook()
    {
        _mouseProcedure = MouseProcedure;
        _keyboardProcedure = KeyboardProcedure;
    }

    public Func<GlobalMouseEvent, bool>? MouseInput { get; set; }
    public Func<GlobalKeyboardEvent, bool>? KeyboardInput { get; set; }

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(HookThread) { IsBackground = true, Name = "WinNotch input hooks" };
        _thread.Start();
        if (!_started.Wait(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("Input hook thread did not start.");
        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            Stop();
            throw new InvalidOperationException("Windows rejected the global input hooks.");
        }
    }

    public void Stop()
    {
        var thread = _thread;
        if (thread == null) return;
        if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(5));
        _thread = null;
        _threadId = 0;
        _started.Reset();
    }

    private void HookThread()
    {
        _threadId = GetCurrentThreadId();
        IntPtr module = GetModuleHandle(null);
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProcedure, module, 0);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProcedure, module, 0);
        _started.Set();
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = _keyboardHook = IntPtr.Zero;
    }

    private IntPtr MouseProcedure(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var value = Marshal.PtrToStructure<LowLevelMouseData>(lParam);
            bool handled = false;
            try
            {
                handled = MouseInput?.Invoke(new GlobalMouseEvent(
                    wParam.ToInt32(), value.Point.X, value.Point.Y,
                    unchecked((int)value.MouseData), (value.Flags & 1) != 0)) == true;
            }
            catch { }
            if (handled) return new IntPtr(1);
        }
        return CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private IntPtr KeyboardProcedure(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var value = Marshal.PtrToStructure<LowLevelKeyboardData>(lParam);
            int message = wParam.ToInt32();
            bool down = message is 0x0100 or 0x0104;
            bool handled = false;
            try
            {
                handled = KeyboardInput?.Invoke(new GlobalKeyboardEvent(
                    (int)value.VirtualKey, (int)value.ScanCode, down,
                    (value.Flags & 0x10) != 0)) == true;
            }
            catch { }
            if (handled) return new IntPtr(1);
        }
        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        _started.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelMouseData
    {
        public Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelKeyboardData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Hwnd;
        public uint Value;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }
    private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProcedure callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
