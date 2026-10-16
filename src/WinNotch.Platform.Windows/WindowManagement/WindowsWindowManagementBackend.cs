using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WinNotch.WindowManagement;

public sealed class WindowsWindowManagementBackend : IWindowManagementBackend
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const uint DwmwaCloaked = 14;
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpAsyncWindowPos = 0x4000;
    private const int SwRestore = 9;
    private const int SwMaximize = 3;
    private const uint WmClose = 0x0010;
    private const uint SmtoBlock = 0x0001;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint CloseTimeoutMilliseconds = 1000;
    private const uint QuitTimeoutMilliseconds = 100;
    private const int MaximumQuitWindows = 32;

    private readonly Dictionary<long, RestorePoint> _restorePoints = [];
    private readonly VirtualDesktopQuery _virtualDesktops = new();
    private bool _disposed;

    public IReadOnlyList<ManagedWindow> GetWindows(bool currentVirtualDesktopOnly = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var windows = new List<ManagedWindow>();
        uint ownProcessId = (uint)Environment.ProcessId;

        EnumWindows((handle, _) =>
        {
            if (!IsCandidate(handle, ownProcessId)) return true;
            bool onCurrentDesktop = _virtualDesktops.IsOnCurrentDesktop(handle);
            if (currentVirtualDesktopOnly && !onCurrentDesktop) return true;

            int titleLength = GetWindowTextLength(handle);
            var title = new StringBuilder(titleLength + 1);
            GetWindowText(handle, title, title.Capacity);
            string windowTitle = title.ToString().Trim();
            if (windowTitle.Length == 0) return true;

            GetWindowThreadProcessId(handle, out uint processId);
            string processName;
            try { processName = Process.GetProcessById((int)processId).ProcessName; }
            catch { processName = "Application"; }

            if (!GetWindowRect(handle, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                return true;

            IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            string monitorName = TryGetMonitor(monitor, out var monitorInfo)
                ? monitorInfo.DeviceName
                : string.Empty;
            windows.Add(new ManagedWindow(
                handle.ToInt64(),
                (int)processId,
                windowTitle,
                processName,
                ToBounds(rect),
                monitorName,
                IsIconic(handle),
                onCurrentDesktop));
            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(window => window.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public WindowOperationResult Activate(long handleValue)
    {
        if (!TryGetWindow(handleValue, out var handle, out var failure)) return failure;
        if (IsIconic(handle)) ShowWindow(handle, SwRestore);
        return SetForegroundWindow(handle)
            ? WindowOperationResult.Completed("Window activated.")
            : WindowOperationResult.Failed("Windows did not allow this app to take foreground focus.");
    }

    public WindowOperationResult ApplyLayout(long handleValue, WindowLayoutPreset preset)
    {
        if (!TryGetWindow(handleValue, out var handle, out var failure)) return failure;
        if (!CaptureRestorePoint(handle, out failure)) return failure;

        if (preset == WindowLayoutPreset.Maximize)
        {
            ShowWindow(handle, SwMaximize);
            return IsZoomed(handle)
                ? WindowOperationResult.Completed("Window maximized.")
                : WindowOperationResult.Failed("The window rejected the maximize request.");
        }

        IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (!TryGetMonitor(monitor, out var monitorInfo))
            return LastError("Could not read the monitor work area.");

        var target = WindowLayoutPlanner.Calculate(ToBounds(monitorInfo.WorkArea), preset);
        if (IsIconic(handle) || IsZoomed(handle)) ShowWindow(handle, SwRestore);
        if (!SetWindowPos(handle, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height,
                SwpNoZOrder | SwpNoActivate | SwpAsyncWindowPos))
            return LastError("The window rejected the layout request.");
        return WindowOperationResult.Completed($"Applied {FormatPreset(preset)} layout.");
    }

    public WindowOperationResult Restore(long handleValue)
    {
        if (!TryGetWindow(handleValue, out var handle, out var failure)) return failure;
        if (!_restorePoints.TryGetValue(handleValue, out var restorePoint))
            return WindowOperationResult.Failed("No WinNotch layout change has been recorded for this window.");
        GetWindowThreadProcessId(handle, out uint currentProcessId);
        if (currentProcessId != restorePoint.ProcessId)
        {
            _restorePoints.Remove(handleValue);
            return WindowOperationResult.Failed("The original window has closed, so its placement cannot be restored.");
        }

        var placement = restorePoint.Placement;
        placement.Length = (uint)Marshal.SizeOf<WindowPlacement>();
        if (!SetWindowPlacement(handle, ref placement))
            return LastError("The window rejected its previous placement.");
        _restorePoints.Remove(handleValue);
        return WindowOperationResult.Completed("Previous window placement restored.");
    }

    public WindowOperationResult MoveToAdjacentMonitor(long handleValue, int direction)
    {
        if (direction == 0)
            return WindowOperationResult.Failed("Choose the previous or next monitor.");
        if (!TryGetWindow(handleValue, out var handle, out var failure)) return failure;
        if (!CaptureRestorePoint(handle, out failure)) return failure;
        if (!GetWindowRect(handle, out var rect)) return LastError("Could not read the window position.");

        var monitors = EnumerateMonitors();
        if (monitors.Count < 2)
            return WindowOperationResult.Failed("No adjacent monitor is available.");
        IntPtr currentHandle = MonitorFromWindow(handle, MonitorDefaultToNearest);
        int currentIndex = monitors.FindIndex(value => value.Handle == currentHandle);
        if (currentIndex < 0) currentIndex = 0;
        int targetIndex = (currentIndex + Math.Sign(direction) + monitors.Count) % monitors.Count;
        var current = monitors[currentIndex].Info.WorkArea;
        var target = monitors[targetIndex].Info.WorkArea;

        bool wasMaximized = IsZoomed(handle);
        if (wasMaximized || IsIconic(handle)) ShowWindow(handle, SwRestore);

        int width = Math.Min(rect.Right - rect.Left, target.Right - target.Left);
        int height = Math.Min(rect.Bottom - rect.Top, target.Bottom - target.Top);
        double relativeX = current.Right == current.Left
            ? 0 : (double)(rect.Left - current.Left) / (current.Right - current.Left);
        double relativeY = current.Bottom == current.Top
            ? 0 : (double)(rect.Top - current.Top) / (current.Bottom - current.Top);
        int left = target.Left + (int)Math.Round(relativeX * Math.Max(0, target.Right - target.Left - width));
        int top = target.Top + (int)Math.Round(relativeY * Math.Max(0, target.Bottom - target.Top - height));
        left = Math.Clamp(left, target.Left, target.Right - width);
        top = Math.Clamp(top, target.Top, target.Bottom - height);

        if (!SetWindowPos(handle, IntPtr.Zero, left, top, width, height,
                SwpNoZOrder | SwpNoActivate | SwpAsyncWindowPos))
            return LastError("The window rejected the monitor move.");
        if (wasMaximized) ShowWindow(handle, SwMaximize);
        return WindowOperationResult.Completed($"Moved window to {monitors[targetIndex].Info.DeviceName}.");
    }

    public WindowOperationResult RequestClose(long handleValue)
    {
        if (!TryGetWindow(handleValue, out var handle, out var failure)) return failure;
        IntPtr result = SendMessageTimeout(
            handle, WmClose, IntPtr.Zero, IntPtr.Zero,
            SmtoBlock | SmtoAbortIfHung, CloseTimeoutMilliseconds, out _);
        return result != IntPtr.Zero
            ? WindowOperationResult.Completed("Close request sent. The application may still ask to save work.")
            : WindowOperationResult.Failed("The window did not accept the close request within one second.");
    }

    public WindowOperationResult RequestQuit(int processId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (processId <= 0 || processId == Environment.ProcessId)
            return WindowOperationResult.Failed("WinNotch cannot request that its own process quit from the switcher.");
        var handles = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out uint candidate);
            if (candidate == (uint)processId && IsWindowVisible(handle)) handles.Add(handle);
            return handles.Count < MaximumQuitWindows;
        }, IntPtr.Zero);
        if (handles.Count == 0)
            return WindowOperationResult.Failed("No visible windows remain for this application.");

        int accepted = 0;
        foreach (var handle in handles)
        {
            if (SendMessageTimeout(handle, WmClose, IntPtr.Zero, IntPtr.Zero,
                    SmtoBlock | SmtoAbortIfHung, QuitTimeoutMilliseconds, out _) != IntPtr.Zero)
                accepted++;
        }
        return accepted > 0
            ? WindowOperationResult.Completed(
                $"Close requested for {accepted} window{(accepted == 1 ? string.Empty : "s")}; unsaved-work prompts remain in control of the application.")
            : WindowOperationResult.Failed("The application did not accept any close requests.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _restorePoints.Clear();
        _virtualDesktops.Dispose();
    }

    private bool TryGetWindow(long value, out IntPtr handle, out WindowOperationResult failure)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        handle = new IntPtr(value);
        if (value == 0 || !IsWindow(handle))
        {
            failure = WindowOperationResult.Failed("The selected window is no longer available.");
            return false;
        }
        GetWindowThreadProcessId(handle, out uint processId);
        if (processId == (uint)Environment.ProcessId)
        {
            failure = WindowOperationResult.Failed("WinNotch does not manage its own windows from the switcher.");
            return false;
        }
        failure = default;
        return true;
    }

    private bool CaptureRestorePoint(IntPtr handle, out WindowOperationResult failure)
    {
        long key = handle.ToInt64();
        GetWindowThreadProcessId(handle, out uint processId);
        if (_restorePoints.TryGetValue(key, out var existing)
            && existing.ProcessId == processId)
        {
            failure = default;
            return true;
        }
        var placement = new WindowPlacement { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(handle, ref placement))
        {
            failure = LastError("Could not save the current window placement.");
            return false;
        }
        _restorePoints[key] = new RestorePoint(processId, placement);
        failure = default;
        return true;
    }

    private static bool IsCandidate(IntPtr handle, uint ownProcessId)
    {
        if (!IsWindowVisible(handle) || GetWindowTextLength(handle) <= 0) return false;
        GetWindowThreadProcessId(handle, out uint processId);
        if (processId == 0 || processId == ownProcessId) return false;
        long exStyle = GetExtendedStyle(handle);
        if ((exStyle & WsExToolWindow) != 0 && (exStyle & WsExAppWindow) == 0) return false;
        if (DwmGetWindowAttribute(handle, DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;
        return true;
    }

    private static long GetExtendedStyle(IntPtr handle)
        => IntPtr.Size == 8
            ? GetWindowLongPtr64(handle, GwlExStyle).ToInt64()
            : GetWindowLong32(handle, GwlExStyle);

    private static List<MonitorEntry> EnumerateMonitors()
    {
        var result = new List<MonitorEntry>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            if (TryGetMonitor(handle, out var info)) result.Add(new MonitorEntry(handle, info));
            return true;
        }, IntPtr.Zero);
        return result
            .OrderBy(value => value.Info.WorkArea.Left)
            .ThenBy(value => value.Info.WorkArea.Top)
            .ToList();
    }

    private static bool TryGetMonitor(IntPtr handle, out MonitorInfo info)
    {
        info = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>(),
            DeviceName = string.Empty
        };
        return handle != IntPtr.Zero && GetMonitorInfo(handle, ref info);
    }

    private static WindowBounds ToBounds(Rect value)
        => new(value.Left, value.Top, value.Right - value.Left, value.Bottom - value.Top);

    private static string FormatPreset(WindowLayoutPreset preset)
        => string.Concat(preset.ToString().Select((character, index) =>
            index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));

    private static WindowOperationResult LastError(string prefix)
    {
        int code = Marshal.GetLastWin32Error();
        return WindowOperationResult.Failed(code == 0
            ? prefix
            : $"{prefix} {new Win32Exception(code).Message}");
    }

    private sealed record MonitorEntry(IntPtr Handle, MonitorInfo Info);
    private sealed record RestorePoint(uint ProcessId, WindowPlacement Placement);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public uint Length;
        public uint Flags;
        public uint ShowCommand;
        public Point MinimumPosition;
        public Point MaximumPosition;
        public Rect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect MonitorArea;
        public Rect WorkArea;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr deviceContext, IntPtr monitorRect, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr handle, ref WindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr handle, ref WindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext, IntPtr clipRect, MonitorCallback callback, IntPtr parameter);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr handle, uint attribute, out int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr handle, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeoutMilliseconds, out UIntPtr result);

    private sealed class VirtualDesktopQuery : IDisposable
    {
        private IVirtualDesktopManager? _manager;

        public VirtualDesktopQuery()
        {
            try
            {
                var type = Type.GetTypeFromCLSID(
                    new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), throwOnError: false);
                if (type != null) _manager = Activator.CreateInstance(type) as IVirtualDesktopManager;
            }
            catch { }
        }

        public bool IsOnCurrentDesktop(IntPtr handle)
        {
            try
            {
                return _manager == null
                    || _manager.IsWindowOnCurrentVirtualDesktop(handle, out int current) < 0
                    || current != 0;
            }
            catch { return true; }
        }

        public void Dispose()
        {
            if (_manager != null && Marshal.IsComObject(_manager))
                Marshal.FinalReleaseComObject(_manager);
            _manager = null;
        }
    }

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out int onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }
}
