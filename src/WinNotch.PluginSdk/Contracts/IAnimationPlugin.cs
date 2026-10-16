using System.Windows;
using System.Windows.Media.Animation;

namespace WinNotch.Plugins;

/// <summary>
/// Interface for plugins that customize or replace WinNotch animations.
/// </summary>
public interface IAnimationPlugin : IPlugin
{
    /// <summary>
    /// Create a custom storyboard for notch expand animation.
    /// </summary>
    Storyboard? CreateExpandAnimation(FrameworkElement target, double fromWidth, double toWidth, double fromHeight, double toHeight);

    /// <summary>
    /// Create a custom storyboard for notch collapse animation.
    /// </summary>
    Storyboard? CreateCollapseAnimation(FrameworkElement target, double fromWidth, double toWidth, double fromHeight, double toHeight);

    /// <summary>
    /// Create a custom storyboard for hover peek animation.
    /// </summary>
    Storyboard? CreatePeekAnimation(FrameworkElement target);

    /// <summary>
    /// Provide custom easing function for spring animations.
    /// </summary>
    IEasingFunction? GetCustomEasingFunction();

    /// <summary>
    /// Animation duration in milliseconds.
    /// </summary>
    double AnimationDurationMs { get; }

    /// <summary>
    /// Whether this plugin completely replaces the default animations.
    /// If false, animations are layered on top of defaults.
    /// </summary>
    bool ReplaceDefaultAnimations { get; }
}
