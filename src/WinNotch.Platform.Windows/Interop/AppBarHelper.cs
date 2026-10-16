using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WinNotch.Helpers;

/// <summary>
/// Reserves a strip of screen real estate at the top edge via the shell AppBar
/// API (SHAppBarMessage) — the same mechanism the taskbar uses — so maximized
/// windows start below the notch instead of underneath it. Fullscreen apps
/// still cover the strip, exactly like they cover the taskbar.
/// Approach inspired by prasundebnath/WinNotch (MIT).
/// </summary>
public sealed class AppBarHelper : IDisposable
{
    private const uint ABM_NEW = 0x0;
    private const uint ABM_REMOVE = 0x1;
    private const uint ABM_QUERYPOS = 0x2;
    private const uint ABM_SETPOS = 0x3;
    private const int ABE_TOP = 1;
    private const int ABN_POSCHANGED = 1;
    private const int CallbackMessage = 0x0400 + 0x0137; // WM_USER + arbitrary id

    private IntPtr _hwnd;
    private HwndSource? _source;
    private bool _registered;
    private RECT _requestedRect;
    private bool _hasRegion;

    /// <summary>Call once after the window handle exists (SourceInitialized).</summary>
    public void Attach(Window window)
    {
        _hwnd = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>
    /// Reserves a strip at the top of the given screen. All values are PHYSICAL
    /// pixels — the AppBar API does not speak DIPs.
    /// </summary>
    public void ReserveTop(System.Drawing.Rectangle screenPhysicalBounds, int stripHeightPhysical)
    {
        if (_hwnd == IntPtr.Zero || stripHeightPhysical <= 0) return;

        EnsureRegistered();

        _requestedRect = new RECT
        {
            left = screenPhysicalBounds.Left,
            top = screenPhysicalBounds.Top,
            right = screenPhysicalBounds.Right,
            bottom = screenPhysicalBounds.Top + stripHeightPhysical
        };
        _hasRegion = true;
        ClaimRegion();
    }

    /// <summary>Gives the reserved space back to the desktop.</summary>
    public void Release()
    {
        _hasRegion = false;
        if (!_registered || _hwnd == IntPtr.Zero) return;

        var abd = NewData();
        SHAppBarMessage(ABM_REMOVE, ref abd);
        _registered = false;
    }

    private void EnsureRegistered()
    {
        if (_registered) return;

        var abd = NewData();
        abd.uCallbackMessage = CallbackMessage;
        SHAppBarMessage(ABM_NEW, ref abd);
        _registered = true;
    }

    private void ClaimRegion()
    {
        int height = _requestedRect.bottom - _requestedRect.top;

        var abd = NewData();
        abd.uEdge = ABE_TOP;
        abd.rc = _requestedRect;

        // Canonical sequence: let the shell adjust the rect (other appbars may
        // already own part of the edge), then claim our height from the result.
        SHAppBarMessage(ABM_QUERYPOS, ref abd);
        abd.rc.bottom = abd.rc.top + height;
        SHAppBarMessage(ABM_SETPOS, ref abd);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Shell layout changed (taskbar moved, another appbar appeared) — re-claim
        if (msg == CallbackMessage && wParam.ToInt32() == ABN_POSCHANGED && _hasRegion)
            ClaimRegion();

        return IntPtr.Zero;
    }

    private APPBARDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd
    };

    public void Dispose()
    {
        Release();
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
}
