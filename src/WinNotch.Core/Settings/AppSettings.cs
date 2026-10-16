using System.Text.Json.Serialization;
using WinNotch.Features;
using WinNotch.Timers;
using WinNotch.Clipboard;

namespace WinNotch.Models;

/// <summary>
/// Hover interaction mode for the notch.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HoverMode
{
    /// <summary>Hover slightly expands, click fully opens.</summary>
    HoverPeekClickOpen,
    /// <summary>Long hover fully opens, click also opens.</summary>
    LongHoverOpen
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppTheme
{
    Dark,
    Light,
    Auto
}

/// <summary>
/// User-configurable settings, persisted to %AppData%/WinNotch/settings.json.
/// </summary>
public class AppSettings
{
    public Dictionary<string, bool> FeatureAvailability { get; set; } = new(StringComparer.Ordinal);
    public TimerPreferences TimerPreferences { get; set; } = new();
    public TimerSession TimerSession { get; set; } = new();
    public string? DownloadsFolder { get; set; }
    public string? PreferredMediaSourceId { get; set; }
    public bool OnlineLyricsEnabled { get; set; }
    public ClipboardPreferences ClipboardPreferences { get; set; } = new();

    // General
    public bool StartOnBoot { get; set; }
    public HoverMode HoverMode { get; set; } = HoverMode.HoverPeekClickOpen;
    public int HoverOpenDelayMs { get; set; } = 100;
    public int HoverCloseDelayMs { get; set; } = 150;
    public int LongHoverDelayMs { get; set; } = 600;

    // Updates
    public bool AutoCheckForUpdates { get; set; } = true;

    // Screens & position
    /// <summary>Where the main island was last docked (persisted across restarts).</summary>
    public NotchDock NotchDockPosition { get; set; } = NotchDock.Top;

    /// <summary>Device name of the screen hosting the main island (null = primary).</summary>
    public string? NotchScreenDevice { get; set; }

    /// <summary>Show an island on every connected screen.</summary>
    public bool ShowOnAllScreens { get; set; }

    /// <summary>When multiple islands are shown, open/close them together.</summary>
    public bool SyncIslandsOpenState { get; set; } = true;

    /// <summary>
    /// Reserve a dedicated strip at the top of the screen (shell AppBar, like the
    /// taskbar) so maximized windows start below the notch. Opt-in.
    /// </summary>
    public bool ReserveSpaceAtTop { get; set; }

    // Appearance
    public AppTheme Theme { get; set; } = AppTheme.Auto;
    public bool AdaptToWindowsTheme { get; set; } = true;
    public double CornerRadiusScale { get; set; } = 1.0;
    public bool ShowShadow { get; set; } = true;

    // Music
    public bool ShowMusicControls { get; set; } = true;
    public bool ShowVisualizer { get; set; } = true;

    // HUD
    public bool ShowVolumeHud { get; set; } = true;
    public bool ShowBrightnessHud { get; set; } = true;

    // Calendar
    public bool ShowCalendar { get; set; } = true;

    // Battery
    public bool ShowBattery { get; set; } = true;

    // Webcam
    public bool ShowWebcam { get; set; } = false;
    public int WebcamFps { get; set; } = 15;

    // Beta features
    public bool UseLiquidGlassTheme { get; set; } = false;

    public AppTheme GetManualTheme()
    {
        return Theme == AppTheme.Light ? AppTheme.Light : AppTheme.Dark;
    }

    public AppTheme GetRequestedTheme()
    {
        return AdaptToWindowsTheme ? AppTheme.Auto : GetManualTheme();
    }

    public bool IsFeatureAvailable(FeatureDescriptor feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return FeatureAvailability.TryGetValue(feature.Id.Value, out var enabled)
            ? enabled
            : feature.EnabledByDefault;
    }

    public void SetFeatureAvailable(FeatureDescriptor feature, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(feature);
        FeatureAvailability[feature.Id.Value] = enabled;
    }

}
