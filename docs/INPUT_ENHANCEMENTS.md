# Input enhancements

Enable **Input enhancements** in the feature catalog, then press
`Ctrl+Alt+I` to configure it. Every rewrite is disabled by default.

Supported policies:

- independent vertical/horizontal scroll reversal and 0.1–5× shaping with
  fractional-delta accumulation;
- delayed focus-follows-mouse;
- temporary Windows mouse speed and acceleration thresholds, restored when the
  feature stops;
- XButton1/XButton2 mappings for browser back/forward, middle click, Command
  Bar, or Quick Panel;
- configurable side-button debounce;
- Right Windows (Super) key mappings for Command Bar, Quick Panel, or Task View;
- delimiter-triggered Unicode text snippets; and
- case-insensitive process exclusions.

The low-level hooks run on a dedicated message-loop thread and immediately pass
unhandled events to the next hook. WinNotch ignores every event marked as
injected, so its own `SendInput` actions cannot recurse. Password detection is
performed off the hook thread through UI Automation. Text expansion resets on
focus changes and is disabled whenever the focused control is a password field
or its process is excluded.

## Security and compatibility limits

Windows integrity levels can prevent a normal user process from controlling an
elevated window. Games and anti-cheat protected processes can reject or remove
global hooks or injected input. WinNotch does not elevate, inject into another
process, install a driver, or attempt a bypass. Add affected applications to
the exclusion list.

- [Low-level mouse hook](https://learn.microsoft.com/windows/win32/winmsg/lowlevelmouseproc)
- [`SystemParametersInfo` mouse settings](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-systemparametersinfow)
- [UI Automation password property](https://learn.microsoft.com/dotnet/api/system.windows.automation.automationelement.ispasswordproperty)
