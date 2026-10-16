using System;
using System.Threading.Tasks;

namespace WinNotch.Plugins;

/// <summary>
/// Base interface that all WinNotch plugins must implement.
/// </summary>
public interface IPlugin : IDisposable
{
    /// <summary>
    /// Unique identifier for the plugin (e.g., "com.example.betterfanimation").
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display name of the plugin.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Plugin version (semantic versioning recommended).
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Plugin author/creator.
    /// </summary>
    string Author { get; }

    /// <summary>
    /// Brief description of what the plugin does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Minimum WinNotch version required (e.g., "0.2.3").
    /// </summary>
    string MinimumWinNotchVersion { get; }

    /// <summary>
    /// Called when the plugin is loaded. Use this to initialize resources.
    /// </summary>
    Task InitializeAsync(IPluginContext context);

    /// <summary>
    /// Called when the plugin is enabled by the user.
    /// </summary>
    Task OnEnableAsync();

    /// <summary>
    /// Called when the plugin is disabled by the user.
    /// </summary>
    Task OnDisableAsync();

    /// <summary>
    /// Called when WinNotch is shutting down or the plugin is being unloaded.
    /// </summary>
    Task ShutdownAsync();
}
