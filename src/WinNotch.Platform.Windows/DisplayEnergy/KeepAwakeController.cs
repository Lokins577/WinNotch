using System.Runtime.InteropServices;
using WinNotch.DisplayEnergy;

namespace WinNotch.Platform.Windows.DisplayEnergy;

public sealed class KeepAwakeController : IDisposable
{
    private readonly AutoResetEvent _changed = new(false);
    private readonly Thread _thread;
    private volatile KeepAwakeMode _requested;
    private volatile bool _stopping;

    public KeepAwakeController()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "WinNotch keep awake" };
        _thread.Start();
    }

    public KeepAwakeMode RequestedMode => _requested;

    public void SetMode(KeepAwakeMode mode)
    {
        _requested = mode;
        _changed.Set();
    }

    private void Run()
    {
        while (!_stopping)
        {
            Apply(_requested);
            _changed.WaitOne();
        }
        Apply(KeepAwakeMode.Off);
    }

    private static void Apply(KeepAwakeMode mode)
    {
        uint flags = 0x80000000;
        if (mode is KeepAwakeMode.System or KeepAwakeMode.SystemAndDisplay) flags |= 0x00000001;
        if (mode == KeepAwakeMode.SystemAndDisplay) flags |= 0x00000002;
        SetThreadExecutionState(flags);
    }

    public void Dispose()
    {
        if (_stopping) return;
        _stopping = true;
        _changed.Set();
        _thread.Join(TimeSpan.FromSeconds(5));
        _changed.Dispose();
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
