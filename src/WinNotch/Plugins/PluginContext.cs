using System;
using System.IO;
using WinNotch.Services;

namespace WinNotch.Plugins;

/// <summary>
/// Capability-oriented adapter between the application and third-party plugins.
/// Concrete windows, settings, and mutable host services stay behind this boundary.
/// </summary>
internal sealed class PluginContext : IPluginContext
{
    private readonly Action<string?> _openConfiguration;

    public IPluginThemeService Theme { get; }
    public IPluginBatteryService Battery { get; }

    public PluginContext(
        ThemeService themeService,
        BatteryService batteryService,
        Action<string?> openConfiguration)
    {
        Theme = new PluginThemeAdapter(themeService);
        Battery = new PluginBatteryAdapter(batteryService);
        _openConfiguration = openConfiguration;
    }

    public string GetPluginDataPath(string pluginId)
    {
        ValidatePluginId(pluginId);

        var basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinNotch",
            "Plugins",
            pluginId);

        Directory.CreateDirectory(basePath);
        return basePath;
    }

    public void OpenConfiguration(string pluginId)
    {
        ValidatePluginId(pluginId);
        _openConfiguration(pluginId);
    }

    public void Log(string message, PluginLogLevel level = PluginLogLevel.Info)
    {
        var prefix = level switch
        {
            PluginLogLevel.Debug => "[DEBUG]",
            PluginLogLevel.Info => "[INFO]",
            PluginLogLevel.Warning => "[WARN]",
            PluginLogLevel.Error => "[ERROR]",
            _ => "[INFO]"
        };

        System.Diagnostics.Debug.WriteLine($"{prefix} [Plugin] {message}");

        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WinNotch",
                "plugin.log");

            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {prefix} {message}\n");
        }
        catch
        {
            // Diagnostics must not crash the plugin host.
        }
    }

    private static void ValidatePluginId(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || pluginId.Length > 128
            || pluginId is "." or ".."
            || pluginId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || pluginId.Contains(Path.DirectorySeparatorChar)
            || pluginId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Plugin ID is not a safe directory name.", nameof(pluginId));
        }
    }

    private sealed class PluginThemeAdapter : IPluginThemeService
    {
        private readonly ThemeService _service;

        public PluginThemeAdapter(ThemeService service)
        {
            _service = service;
        }

        public bool IsLight => _service.IsLight;

        public event Action? ThemeChanged
        {
            add => _service.ThemeChanged += value;
            remove => _service.ThemeChanged -= value;
        }
    }

    private sealed class PluginBatteryAdapter : IPluginBatteryService
    {
        private readonly BatteryService _service;

        public PluginBatteryAdapter(BatteryService service)
        {
            _service = service;
        }

        public int ChargePercent => _service.ChargePercent;
        public bool IsCharging => _service.IsCharging;
        public bool HasBattery => _service.HasBattery;
        public TimeSpan TimeRemaining => _service.TimeRemaining;
    }
}
