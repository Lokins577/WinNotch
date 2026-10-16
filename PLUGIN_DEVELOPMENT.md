# WinNotch Plugin Development Guide

This guide covers the current WinNotch plugin workflow: how to reference the plugin API, build a plugin, test it locally, and get it listed in the in-app browser.

If you want the broader user and developer walkthrough, see the live documentation site:

- `https://n3uralcreativity.github.io/WinNotch/documentation/`

## What a WinNotch plugin can do

WinNotch plugins can extend the app in several ways:

- add visible UI to the closed or expanded notch
- add accessory content beside built-in expanded content
- provide a different vertical layout when the notch is docked on the side
- run background services or integrations
- replace or extend notch animations
- expose a configuration form inside Plugin Manager

Most plugins inherit from `PluginBase` and then implement one or more specialized interfaces.

## Plugin interfaces

These are the main extension points in the current app:

- `IPlugin`
- `IUIPlugin`
- `IServicePlugin`
- `IAnimationPlugin`
- `IConfigurablePlugin`

UI plugins can return elements for these locations:

- `ClosedContent`
- `OpenContent`
- `OpenAccessory`
- `VerticalOpenContent`
- `CustomTab`
- `Settings`
- `Overlay`

The live contracts are in `src/WinNotch.PluginSdk/`.

## Choose a development setup

There are two good ways to reference WinNotch when building plugins.

### Option 1: Use the GitHub Packages SDK

This is the easiest setup if you are building a plugin outside this repository.

Add the GitHub Packages source:

```bash
dotnet nuget add source "https://nuget.pkg.github.com/N3uralCreativity/index.json" \
  --name github-winnotch \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_GITHUB_PAT \
  --store-password-in-clear-text
```

Then install the SDK package:

```bash
dotnet add package WinNotch.PluginSdk --version 0.7.2 --source github-winnotch
```

Your personal access token needs `read:packages`.

### Option 2: Reference the SDK project directly

This is the best setup when you are already working inside the WinNotch repository and want easy access to the example plugins.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\src\WinNotch.PluginSdk\WinNotch.PluginSdk.csproj" />
  </ItemGroup>
</Project>
```

## Minimal plugin project

Whether you use the package or a direct project reference, your plugin project should look roughly like this:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="WinNotch.PluginSdk" Version="0.7.2" />
  </ItemGroup>
</Project>
```

## Minimal plugin class

```csharp
using System.Windows;
using System.Windows.Controls;
using WinNotch.Models;
using WinNotch.Plugins;

namespace HelloWorldPlugin;

[WinNotchPlugin("com.example.helloworld", "Hello World", "1.0.0", "Your Name")]
public sealed class HelloWorldPlugin : PluginBase, IUIPlugin
{
    public override string Id => "com.example.helloworld";
    public override string Name => "Hello World";
    public override string Version => "1.0.0";
    public override string Author => "Your Name";
    public override string Description => "A simple test plugin.";
    public override string MinimumWinNotchVersion => "0.7.2";

    public UIElement? GetUIElement(UIPluginLocation location)
    {
        return location switch
        {
            UIPluginLocation.ClosedContent => new TextBlock { Text = "Hi" },
            UIPluginLocation.OpenContent => new TextBlock { Text = "Hello from WinNotch" },
            _ => null
        };
    }

    public void OnNotchStateChanged(NotchState newState)
    {
    }
}
```

Build it with:

```bash
dotnet build -c Release
```

## Test a plugin locally

For a manual local install, place the DLL under:

```text
%AppData%\WinNotch\Plugins\com.example.helloworld\HelloWorldPlugin.dll
```

Restart WinNotch after copying the file.

If you also include a `manifest.json` beside the DLL, WinNotch can show better installed metadata and compare versions for updates.

## Plugin context

Every plugin receives an `IPluginContext` during initialization. It exposes stable, capability-oriented APIs rather than application windows or mutable host internals.

```csharp
public interface IPluginContext
{
    IPluginThemeService Theme { get; }
    IPluginBatteryService Battery { get; }
    string GetPluginDataPath(string pluginId);
    void OpenConfiguration(string pluginId);
    void Log(string message, PluginLogLevel level = PluginLogLevel.Info);
}
```

Use `GetPluginDataPath(Id)` for plugin-owned files such as `settings.json`, caches, or tokens.

Plugins currently run in the WinNotch process with normal .NET permissions.
Manifest permission declarations tell users what a plugin intends to do; they
do not sandbox filesystem, network, process, or native API access. Only install
plugins whose source and publisher you trust.

## UI plugin guidance

`IUIPlugin` is the most common interface:

```csharp
public interface IUIPlugin : IPlugin
{
    UIElement? GetUIElement(UIPluginLocation location);
    void OnNotchStateChanged(NotchState newState);
}
```

Layout advice:

- use `OpenAccessory` for small inline content beside built-in expanded content
- use `VerticalOpenContent` when a side-docked notch needs a different composition
- avoid assuming a fixed horizontal canvas unless your plugin is explicitly top-only
- react to `Context.Theme.ThemeChanged` so your UI stays readable in both themes

## Service and animation plugins

Use `IServicePlugin` for background work:

```csharp
public interface IServicePlugin : IPlugin
{
    Task StartServiceAsync();
    Task StopServiceAsync();
}
```

Use `IAnimationPlugin` when your plugin primarily controls notch motion:

```csharp
public interface IAnimationPlugin : IPlugin
{
    Storyboard? CreateExpandAnimation(FrameworkElement target, double fromWidth, double toWidth, double fromHeight, double toHeight);
    Storyboard? CreateCollapseAnimation(FrameworkElement target, double fromWidth, double toWidth, double fromHeight, double toHeight);
    Storyboard? CreatePeekAnimation(FrameworkElement target);
    IEasingFunction? GetCustomEasingFunction();
    double AnimationDurationMs { get; }
    bool ReplaceDefaultAnimations { get; }
}
```

## Configurable plugins

If your plugin needs user input, implement `IConfigurablePlugin`.

```csharp
public interface IConfigurablePlugin : IPlugin
{
    PluginConfigurationDefinition GetConfigurationDefinition();
    Task<PluginConfigurationResult> ApplyConfigurationAsync(IReadOnlyDictionary<string, string?> values);
}
```

The Plugin Manager will render a built-in configuration panel using the fields you describe. Each field can include:

- label
- placeholder
- help text
- required state
- choice options

Typical use cases:

- API keys
- account or workspace identifiers
- provider selection
- timer values
- prompt templates

## Packaging and publishing

The normal plugin release flow is:

1. Build your plugin DLL in `Release`.
2. Upload that DLL to a public release URL, usually a GitHub Release.
3. Compute its SHA-256 hash.
4. Create or update a plugin manifest entry.
5. Add that entry to `WinNotch-Plugins/library.json` if you want it to appear in the in-app browser.

Example manifest:

```json
{
  "id": "com.example.myplugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "author": "Your Name",
  "description": "Does something useful in the notch.",
  "minimumWinNotchVersion": "0.7.2",
  "downloadUrl": "https://github.com/you/myplugin/releases/download/v1.0.0/MyPlugin.dll",
  "homepage": "https://github.com/you/myplugin",
  "iconUrl": "",
  "category": "Productivity",
  "permissions": ["network"],
  "dependencies": [],
  "sha256": "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
  "releaseDate": "2026-04-23T00:00:00Z",
  "isVerified": false
}
```

## How a plugin gets into the browser

The WinNotch app does not search GitHub releases directly. The in-app browser reads plugin metadata from the `WinNotch-Plugins` repository's `library.json`.

That means a plugin appears in the browser only when:

- the DLL is publicly downloadable
- the manifest entry is present in `WinNotch-Plugins/library.json`
- the entry passes the library validation checks in that repository

The plugin library repo now includes:

- `library.schema.json`
- `CONTRIBUTING.md`
- a validation script at `scripts/validate-library.ps1`
- a GitHub Actions workflow that validates `library.json` on push and pull request

## Release checklist

Before opening a PR to `WinNotch-Plugins`, confirm:

- your plugin ID is unique
- your version is updated
- `minimumWinNotchVersion` matches the APIs you actually use
- `downloadUrl` points directly to the DLL
- `homepage` points to the repo or project page
- the SHA-256 hash matches the released DLL
- your category is one of the supported values
- the plugin has been tested in WinNotch

Supported categories:

- `Animation`
- `Integration`
- `Productivity`
- `Media`
- `SystemUtility`
- `Theme`
- `Widget`
- `Fun`
- `Other`

## Examples in this repository

Working examples live under `Examples/Plugins/`, including:

- `BetterAnimationPlugin`
- `ChatGPTAddonPlugin`
- `PetWidgetPlugin`
- `WeatherWidgetPlugin`
- `TodoPeekPlugin`

There is also a sample browser manifest file at:

- `Examples/Plugins/example-library.json`

## Best practices

- use semantic versioning for your plugin versions
- keep plugin-specific files inside the folder returned by `GetPluginDataPath`
- avoid blocking the UI thread
- clean up timers, subscriptions, and unmanaged resources in `ShutdownAsync` and `Dispose`
- handle theme changes and layout changes explicitly
- document any required setup clearly if your plugin is configurable
- raise `MinimumWinNotchVersion` when you depend on newer WinNotch APIs

## Help

- Main repo issues: `https://github.com/N3uralCreativity/WinNotch/issues`
- Plugin browser listings: `https://github.com/N3uralCreativity/WinNotch-Plugins`
