namespace WinNotch.WindowManagement;

public readonly record struct WindowBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public enum WindowLayoutPreset
{
    LeftHalf,
    RightHalf,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    LeftThird,
    CenterThird,
    RightThird,
    Maximize
}

public sealed record ManagedWindow(
    long Handle,
    int ProcessId,
    string Title,
    string ProcessName,
    WindowBounds Bounds,
    string MonitorName,
    bool IsMinimized,
    bool IsOnCurrentVirtualDesktop);

public readonly record struct WindowOperationResult(bool Success, string Message)
{
    public static WindowOperationResult Completed(string message) => new(true, message);
    public static WindowOperationResult Failed(string message) => new(false, message);
}

public interface IWindowManagementBackend : IDisposable
{
    IReadOnlyList<ManagedWindow> GetWindows(bool currentVirtualDesktopOnly = true);
    WindowOperationResult Activate(long handle);
    WindowOperationResult ApplyLayout(long handle, WindowLayoutPreset preset);
    WindowOperationResult Restore(long handle);
    WindowOperationResult MoveToAdjacentMonitor(long handle, int direction);
    WindowOperationResult RequestClose(long handle);
    WindowOperationResult RequestQuit(int processId);
}
