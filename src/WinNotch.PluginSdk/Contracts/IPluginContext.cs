using System;

namespace WinNotch.Plugins;

/// <summary>
/// Stable, capability-oriented host surface available to plugins.
/// Application windows, settings models, and concrete system services are
/// intentionally not exposed across the plugin boundary.
/// </summary>
public interface IPluginContext
{
    /// <summary>Current application theme and theme-change notifications.</summary>
    IPluginThemeService Theme { get; }

    /// <summary>Read-only battery information suitable for plugin UI.</summary>
    IPluginBatteryService Battery { get; }

    /// <summary>Returns a private persistent data directory for the plugin.</summary>
    string GetPluginDataPath(string pluginId);

    /// <summary>Opens this plugin's configuration page in the host.</summary>
    void OpenConfiguration(string pluginId);

    /// <summary>Writes a message to the host diagnostic log.</summary>
    void Log(string message, PluginLogLevel level = PluginLogLevel.Info);
}

public interface IPluginThemeService
{
    bool IsLight { get; }
    event Action? ThemeChanged;
}

public interface IPluginBatteryService
{
    int ChargePercent { get; }
    bool IsCharging { get; }
    bool HasBattery { get; }
    TimeSpan TimeRemaining { get; }
}

public enum PluginLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}
