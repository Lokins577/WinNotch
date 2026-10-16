using System.Windows;

namespace WinNotch.Plugins;

/// <summary>
/// Interface for plugins that provide UI components.
/// </summary>
public interface IUIPlugin : IPlugin
{
    /// <summary>
    /// Get the UI element to be injected into the notch.
    /// Return null if the plugin doesn't need to display UI.
    /// </summary>
    UIElement? GetUIElement(UIPluginLocation location);

    /// <summary>
    /// Called when the notch state changes (closed, peeking, open).
    /// </summary>
    void OnNotchStateChanged(Models.NotchState newState);
}

/// <summary>
/// Locations where a UI plugin can inject content.
/// </summary>
public enum UIPluginLocation
{
    /// <summary>Content shown in the closed/compact notch.</summary>
    ClosedContent,

    /// <summary>Content shown in the expanded notch (main area).</summary>
    OpenContent,

    /// <summary>Accessory content shown inline in the expanded top-row layout.</summary>
    OpenAccessory,

    /// <summary>Content shown only in the vertical expanded notch layout.</summary>
    VerticalOpenContent,

    /// <summary>Additional tab in the expanded view.</summary>
    CustomTab,

    /// <summary>Settings panel for the plugin.</summary>
    Settings,

    /// <summary>Custom overlay layer (highest priority).</summary>
    Overlay
}
