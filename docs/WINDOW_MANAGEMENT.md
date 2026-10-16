# Window management

Enable **Window management** in the feature catalog, then press `Ctrl+Alt+W`
to open the switcher. The feature is disabled by default because it can move or
close windows belonging to other applications.

## Included behavior

- enumerates visible top-level application windows and excludes WinNotch;
- filters to the current Windows virtual desktop by default;
- renders the selected window through a live DWM thumbnail, using the same
  public preview mechanism that underpins taskbar-style previews;
- activates minimized or visible windows;
- applies halves, quarters, thirds, and maximize layouts within the monitor
  work area, so the taskbar remains unobscured;
- remembers the first placement changed by WinNotch and can restore it during
  the current session;
- moves a window to the previous or next monitor while preserving its relative
  size and position; and
- requests that one window or every visible window for an application close.

Layout and monitor operations can be rejected by elevated applications or by
windows that control their own position. WinNotch reports that failure and does
not retry through elevation or input injection.

## Safety and platform boundaries

Close and quit actions send bounded `WM_CLOSE` requests. A single window gets a
one-second hung-window timeout; confirmed app-wide quit is capped at 32 visible
windows and roughly 3.2 seconds total. They never call `TerminateProcess`; save
prompts and cancellation remain under the target application's control.

Virtual-desktop awareness uses only the documented `IVirtualDesktopManager`
interface. WinNotch can determine whether a window is on the current desktop,
but it does not enumerate or switch desktops through private Explorer APIs. If
the documented COM service is unavailable, filtering safely degrades to showing
the normal visible-window set.

The preview uses the documented DWM thumbnail relationship and is released when
the selection or switcher closes:

- [DWM thumbnail overview](https://learn.microsoft.com/windows/win32/dwm/thumbnail-ovw)
- [`DwmUpdateThumbnailProperties`](https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmupdatethumbnailproperties)
- [`IVirtualDesktopManager`](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nn-shobjidl_core-ivirtualdesktopmanager)
- [`SetWindowPlacement`](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowplacement)
