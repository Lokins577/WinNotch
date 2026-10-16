# WinNotch Plugin Template

Quick-start template for building WinNotch plugins.

## Setup

1. Copy this folder to your desired location
2. Rename the project (`.csproj`, namespace, class name)
3. Update the `ProjectReference` path in the `.csproj` to point to your local WinNotch source
4. Update the plugin metadata (`Id`, `Name`, `Author`, etc.)

## Build

```bash
dotnet build -c Release
```

## Install

Copy the output DLL from `bin/Release/net8.0-windows/` to:

```
%AppData%\WinNotch\Plugins\YourPluginName\
```

Restart WinNotch. Your plugin will be auto-discovered and enabled.

## Plugin Types

| Interface | Purpose |
|-----------|---------|
| `IPlugin` | Base — lifecycle hooks only |
| `IUIPlugin` | Inject UI into notch at various locations |
| `IServicePlugin` | Run background tasks |
| `IAnimationPlugin` | Override notch expand/collapse/peek animations |

## UIPluginLocation

| Location | Description |
|----------|-------------|
| `ClosedContent` | Compact notch bar (alongside clock/battery) |
| `OpenContent` | Expanded notch main area |
| `CustomTab` | Additional tab in expanded view |
| `Settings` | Plugin settings panel |
| `Overlay` | Highest-priority overlay layer |

## Available capabilities via IPluginContext

- `Theme` — Read the current light/dark state and observe theme changes
- `Battery` — Read battery charge, charging state, availability, and time remaining
- `GetPluginDataPath` — Store plugin-owned files
- `OpenConfiguration` — Open the plugin's host-managed configuration page
- `Log` — Write diagnostic messages through the host

## Data Storage

```csharp
var dataPath = Context.GetPluginDataPath(Id);
// Returns: %AppData%\WinNotch\Plugins\{pluginId}\
```

## Logging

```csharp
Context.Log("Something happened", PluginLogLevel.Info);
// Writes to: %AppData%\WinNotch\plugin.log
```

See [PLUGIN_DEVELOPMENT.md](../../PLUGIN_DEVELOPMENT.md) for the full guide.
