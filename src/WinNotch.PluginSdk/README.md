# WinNotch Plugin SDK

`WinNotch.PluginSdk` is the standalone contract package for building WinNotch plugins.

## What it contains

- The public plugin contracts such as:
  - `IPlugin`
  - `IUIPlugin`
  - `IServicePlugin`
  - `IAnimationPlugin`
  - `IConfigurablePlugin`
  - `PluginBase`
  - `PluginManifest`
- Narrow host capabilities for theme, battery, storage, configuration, and logging

## Install from GitHub Packages

First add the GitHub Packages NuGet source:

```bash
dotnet nuget add source "https://nuget.pkg.github.com/N3uralCreativity/index.json" \
  --name github-winnotch \
  --username YOUR_GITHUB_USERNAME \
  --password YOUR_GITHUB_PAT \
  --store-password-in-clear-text
```

Your personal access token needs `read:packages`.

Then add the package:

```bash
dotnet add package WinNotch.PluginSdk --version 0.7.2 --source github-winnotch
```

## Important note

This package is for plugin development. End users should still install WinNotch itself from GitHub Releases.

At runtime, your plugin loads inside WinNotch and uses the SDK contracts shipped with the app. Keep your plugin's `MinimumWinNotchVersion` aligned with the app version you target. Plugins do not receive the host window or mutable internal services; use only capabilities exposed by `IPluginContext`.
