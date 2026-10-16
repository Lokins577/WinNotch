# Display, energy, and hardware

Enable **Display and energy** in the feature catalog, then press
`Ctrl+Alt+E`.

## Energy and display controls

- Keep Awake can hold the system awake, or hold both the system and display
  awake. Fullscreen and active WinNotch recording can enable a temporary rule.
  The request runs on a dedicated thread and is cleared on disable/exit.
- Internal-panel brightness continues through the existing
  `WmiMonitorBrightness` provider.
- External-monitor brightness uses only the public high-level DDC/CI API after
  the monitor reports `MC_CAPS_BRIGHTNESS` **and** the user explicitly marks
  that exact monitor ID as physically validated.
- Software dimming uses click-through, no-activate, topmost black overlays on
  each display. The overlays are excluded from screen capture and are removed
  when opacity becomes zero or the feature stops.

Microsoft warns that many displays incompletely implement VESA MCCS and that
arbitrary monitor writes can have undefined behavior. WinNotch therefore never
automatically enables DDC/CI writes, never saves monitor settings to firmware,
and exposes no factory-reset/VCP command surface.

## Bluetooth sleep and hardware providers

The optional sleep policy requests Windows radio permission before use. On
suspend it can request Bluetooth Off; the restore mode remembers whether any
Bluetooth radio was on and requests On after resume. Hardware switches and
system policy remain authoritative, and denied requests are reported.

Temperature uses the highest valid ACPI thermal-zone reading and explicitly
does not label it CPU package temperature. Fan control has a provider contract
requiring both an exact supported hardware ID and a user-validated provider ID.
No generic embedded-controller writer is built in, so unsupported machines
remain read-only.

- [`SetThreadExecutionState`](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate)
- [DDC/CI brightness](https://learn.microsoft.com/windows/win32/api/highlevelmonitorconfigurationapi/nf-highlevelmonitorconfigurationapi-getmonitorbrightness)
- [Physical monitor enumeration](https://learn.microsoft.com/windows/win32/api/physicalmonitorenumerationapi/nf-physicalmonitorenumerationapi-getphysicalmonitorsfromhmonitor)
- [Windows radio permission and state](https://learn.microsoft.com/uwp/api/windows.devices.radios.radio)
