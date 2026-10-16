# WinNotch architecture

WinNotch uses explicit project boundaries. New code should follow the dependency
direction below.

```text
WinNotch (WPF application and composition root)
  |-- WinNotch.Core (platform-neutral domain and application code)
  |-- WinNotch.Platform.Windows (Win32, WinRT, COM, storage, and WPF integration)
  |-- WinNotch.PluginSdk (stable third-party contracts)
  `-- WinNotch.PluginHost (plugin loading, lifecycle, and online library)
```

## Project responsibilities

### `WinNotch`

Owns the executable, WPF views, application lifecycle, settings UI, built-in
feature composition, presentation services, and adapters between the app and
plugin capabilities. Platform APIs should not be added directly here when they
can be isolated behind the Windows platform project.

### `WinNotch.Core`

Owns deterministic, platform-neutral behavior. It must remain buildable as
plain `net8.0` and must not reference WPF, WinForms, WinRT, the registry, COM,
or native Windows DLLs.

### `WinNotch.Platform.Windows`

Owns Windows-specific integration: window handles, monitor and DPI handling,
global input registration, screen capture, audio, shell integration, and other
Win32/WinRT/COM adapters. It must not contain application views or feature
policy.

Public Windows service types moved out of `WinNotch.dll` use assembly type
forwarders. This preserves binary compatibility for existing plugins while new
builds reference the dedicated platform assembly directly.

### `WinNotch.PluginSdk`

Owns the plugin interfaces, configuration schema, manifest model, notch state,
and narrow host capabilities. It does not reference the application, plugin
host, or Windows platform implementation. Example plugins reference only this
project.

### `WinNotch.PluginHost`

Owns plugin discovery, lifecycle, state, package download, and hash validation.
It references PluginSdk, but it does not reference the WPF application. Host
version and capabilities are injected by the composition root.

## Feature layout

Large built-in capabilities should be added as cohesive feature modules rather
than extending `NotchWindow` or the composition root. A feature should keep its
model and policy in Core where possible, its operating-system adapter in the
Windows platform project, and its WPF surface in the application project.

`WinNotch.Core/Features` owns stable feature IDs, descriptors, the built-in
catalog, persisted availability policy, and the asynchronous lifecycle runtime.
Planned features may appear in the catalog but cannot register a runtime module
until their maturity changes to Available or Experimental.

Timer policy lives in `WinNotch.Core/Timers`; the WPF timer service supplies the
dispatcher heartbeat, persistence, and alarm, while views only render snapshots
and send commands. Running sessions persist a UTC anchor so sleep and app
restarts do not introduce one-tick-per-second drift.

Legacy public type names moved out of `WinNotch.dll` are forwarded to their new
assemblies where practical. The 0.7 PluginSdk intentionally narrows
`IPluginContext`; plugins that used removed host internals must migrate. New
plugins compile only against PluginSdk and cannot access those internals through
the host contract.

Plugins still execute in-process with normal .NET trust. Manifest permissions
are disclosure metadata, not a security sandbox. Untrusted plugins require a
future out-of-process host; the capability API reduces coupling but does not by
itself restrict operating-system access.

### `NotchWindow` partial files

The XAML code-behind remains one WPF type, but responsibilities are separated so
new work does not return to a single multi-thousand-line file:

- `NotchWindow.xaml.cs`: lifecycle, window layout, animation, and interaction.
- `NotchWindow.Services.cs`: binding shared services to the view.
- `NotchWindow.Plugins.cs`: plugin lifecycle and UI hosting.
- `NotchWindow.Settings.cs`: integrated settings state and application.
- `NotchWindow.SideContent.cs`: vertical-dock media content.
- `NotchWindow.Shelf.cs`: file drag-and-drop behavior.
- `NotchWindow.Effects.cs`: Liquid Glass capture and visual effects.

## Dependency rules

- Core references no other WinNotch project.
- Platform.Windows may reference Core when a platform adapter implements a
  Core contract.
- PluginSdk references no other WinNotch project.
- PluginHost references PluginSdk only.
- The WPF application may reference all lower layers and is the composition root.
- Feature code must not load optional services merely because a view exists.
- Elevated or failure-prone native work should eventually run out of process.
