# System Monitor

Enable **System monitor** in the feature catalog, then press `Ctrl+Alt+M` to
open the dashboard. Sampling runs only while the feature is enabled and stops
through Feature Runtime when disabled.

The dashboard keeps an in-memory rolling history, shows top processes, and can
raise tray balloon alerts after CPU, memory, or GPU usage stays above a chosen
threshold. Alerts fire once per sustained episode and re-arm only after the
metric falls below the threshold. No metric, process name, or device name is
uploaded.

## Providers and confidence

Every reading carries one of four states: `Available`, `Estimated`,
`Unavailable`, or `Unsupported`. A missing hardware provider is never rendered
as zero.

| Metric | Provider | Confidence and limitation |
| --- | --- | --- |
| CPU | `GetSystemTimes` deltas | System-wide, available after two samples |
| Memory | `GlobalMemoryStatusEx` | Physical-memory usage |
| Network | active adapter byte deltas | Aggregate upload/download; no per-process claim |
| Disk capacity | fixed-drive capacity | Aggregate space used |
| Disk throughput | Windows formatted physical-disk counter | Aggregate transfer rate |
| GPU | GPU Engine formatted counters | Estimated sum of 3D engines, capped at 100% |
| Process CPU/memory/I/O | process time, working set, `GetProcessIoCounters` | I/O is process accounting, not a disk-only or network-only claim |
| Battery level | Windows power status | Unsupported on desktops without a battery |
| Battery health/power | firmware WMI battery providers | Estimated and commonly unavailable on desktops |
| Temperature | ACPI thermal zones | Estimated; explicitly not labelled CPU package temperature |
| Devices | present PnP Bluetooth/USB/audio/monitor devices | Local display sample capped at 20 entries |

The collector follows Microsoft's guidance that rate counters require multiple
samples. Direct APIs are used for CPU and memory; WMI formatted performance
classes are used only where Windows has no similarly narrow managed surface.

- [`GetProcessIoCounters`](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-getprocessiocounters)
- [About performance counters](https://learn.microsoft.com/windows/win32/perfctrs/about-performance-counters)
- [Collecting performance data](https://learn.microsoft.com/windows/win32/perfctrs/collecting-performance-data)
- [`Win32_Battery`](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-battery)
