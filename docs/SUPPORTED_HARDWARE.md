# Supported hardware and capability matrix

| Capability | Supported path | Fallback / boundary |
| --- | --- | --- |
| Internal brightness | `WmiMonitorBrightness` laptops/panels | Hidden when firmware exposes no provider |
| External brightness | VESA MCCS DDC/CI with reported brightness capability and per-monitor user validation | Read-only Unsupported; no generic VCP writes |
| Temperature | ACPI thermal-zone WMI | Estimated firmware-zone label; never claimed as CPU package temperature |
| Fan control | Installed provider declaring exact hardware IDs plus explicit user validation | No built-in EC writer; Unsupported |
| Bluetooth sleep | Windows Radio API after user/system permission | Hardware switches and policy remain authoritative |
| Process audio | Windows build 20348+ process-loopback virtual device | Unsupported on earlier builds |
| Notification inbox | Signed MSIX identity, `uap3:userNotificationListener`, and user consent | Inno/unpackaged build reports Requires package identity |
| GPU utilization | Windows GPU Engine formatted counters | Estimated, capped at 100%; provider absence is Unsupported |
| Camera/microphone | Active Windows endpoint/device plus privacy permission | Windows privacy settings are authoritative |
| Scrolling capture | Target exposes UI Automation `ScrollPattern` | Custom/DRM surfaces are not bypassed |

Anti-cheat, elevated windows, protected content, vendor embedded controllers,
and undocumented shell/audio policy interfaces are explicitly outside the
generic support boundary.
