using System.IO;
using System.Text.Json;
using WinNotch.Models;
using WinNotch.Settings;
using WinNotch.QuickTools;
using WinNotch.SystemMonitoring;
using WinNotch.AudioControl;
using WinNotch.Capture;
using WinNotch.MediaTools;
using WinNotch.AppManagement;

namespace WinNotch.Platform.Windows.Settings;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private const string StartupKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "WinNotch";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsDirectory;
    private readonly string _settingsPath;
    private readonly Func<string?> _processPath;

    public JsonAppSettingsStore()
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinNotch"),
            () => Environment.ProcessPath)
    {
    }

    public JsonAppSettingsStore(string settingsDirectory, Func<string?>? processPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        _settingsDirectory = Path.GetFullPath(settingsDirectory);
        _settingsPath = Path.Combine(_settingsDirectory, "settings.json");
        _processPath = processPath ?? (() => Environment.ProcessPath);
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            using var document = JsonDocument.Parse(json);
            bool hasAdaptSetting = document.RootElement.TryGetProperty(nameof(AppSettings.AdaptToWindowsTheme), out _);

            if (!hasAdaptSetting)
                settings.AdaptToWindowsTheme = settings.Theme == AppTheme.Auto;

            if (settings.Theme == AppTheme.Auto)
                settings.Theme = AppTheme.Dark;

            settings.RadialMenu ??= RadialMenuConfiguration.CreateDefault();
            settings.RadialMenu.Normalize();
            settings.SystemMonitorPreferences ??= new SystemMonitorPreferences();
            settings.SystemMonitorPreferences.Normalize();
            settings.AudioControlPreferences ??= new AudioControlPreferences();
            settings.AudioControlPreferences.Normalize();
            settings.CapturePreferences ??= new CapturePreferences();
            settings.CapturePreferences.Normalize();
            settings.RecordingPreferences ??= new RecordingPreferences();
            settings.RecordingPreferences.Normalize();
            settings.AppManagementPreferences ??= new AppManagementPreferences();
            settings.AppManagementPreferences.Normalize();

            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(_settingsDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tempPath = _settingsPath + ".tmp";

        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _settingsPath, overwrite: true);
    }

    public void ApplyStartOnBoot(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupKeyPath, true);
            if (key == null) return;

            if (enabled)
            {
                var exePath = _processPath();
                if (!string.IsNullOrWhiteSpace(exePath))
                    key.SetValue(StartupValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(StartupValueName, false);
            }
        }
        catch
        {
            // Startup registration is best effort; settings still remain saved.
        }
    }
}
