using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinNotch.Models;
using WinNotch.Plugins;
using WinNotch.Helpers;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private async System.Threading.Tasks.Task InitializePluginsAsync()
    {
        try
        {
            _pluginContext = new PluginContext(
                _themeService,
                _batteryService,
                OpenPluginManager);

            _pluginManager = new PluginManager(_pluginContext, AppInfo.Version);
            _pluginLibraryService = new PluginLibraryService(AppInfo.Version);

            await _pluginManager.LoadAllPluginsAsync();

            // Inject UI plugin elements into the notch
            InjectPluginUI();

            // Enable/disable now applies live — re-inject without a restart
            _pluginManager.PluginStateChanged += () => Dispatcher.Invoke(InjectPluginUI);

            // Wire settings "Manage Plugins" button
            InlineSettings.ManagePluginsRequested += OpenPluginManager;
            VerticalInlineSettings.ManagePluginsRequested += OpenPluginManager;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Plugin system initialization failed: {ex.Message}");
        }
    }

    private void InjectPluginUI()
    {
        if (_pluginManager == null) return;

        PluginClosedPanel.Children.Clear();
        PluginOpenPanel.Children.Clear();
        PluginAccessoryPanel.Children.Clear();
        PluginAccessoryPanel.Visibility = Visibility.Collapsed;
        VerticalPluginClosedPanel.Children.Clear();
        VerticalPluginOpenPanel.Children.Clear();

        foreach (var uiPlugin in _pluginManager.GetUIPlugins())
        {
            try
            {
                var closedElement = uiPlugin.GetUIElement(UIPluginLocation.ClosedContent);
                if (closedElement != null)
                {
                    if (_dock == NotchDock.Top)
                        PluginClosedPanel.Children.Add(closedElement);
                    else
                        VerticalPluginClosedPanel.Children.Add(closedElement);
                }

                if (_dock == NotchDock.Top)
                {
                    var accessoryElement = uiPlugin.GetUIElement(UIPluginLocation.OpenAccessory);
                    if (accessoryElement != null)
                    {
                        PluginAccessoryPanel.Children.Add(WrapAccessoryElement(accessoryElement));
                        PluginAccessoryPanel.Visibility = Visibility.Visible;
                    }

                    var openElement = uiPlugin.GetUIElement(UIPluginLocation.OpenContent);
                    if (openElement != null)
                        PluginOpenPanel.Children.Add(openElement);
                }
                else
                {
                    var verticalOpenElement = uiPlugin.GetUIElement(UIPluginLocation.VerticalOpenContent)
                        ?? uiPlugin.GetUIElement(UIPluginLocation.OpenContent);
                    if (verticalOpenElement != null)
                        VerticalPluginOpenPanel.Children.Add(verticalOpenElement);
                }

                // Notify initial state
                uiPlugin.OnNotchStateChanged(_vm.NotchState);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to inject UI for plugin {uiPlugin.Name}: {ex.Message}");
            }
        }

        RefreshPluginHostLayout();
    }

    private static UIElement WrapAccessoryElement(UIElement element)
    {
        var host = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        host.Children.Add(new Border
        {
            Width = 1,
            Margin = new Thickness(8, 4, 8, 4),
            Background = (System.Windows.Media.Brush?)Application.Current?.TryFindResource("SeparatorBrush")
                ?? new SolidColorBrush(Color.FromRgb(72, 72, 76))
        });
        host.Children.Add(element);
        return host;
    }

    private void NotifyPluginsStateChanged(NotchState state)
    {
        if (_pluginManager == null) return;
        foreach (var uiPlugin in _pluginManager.GetUIPlugins())
        {
            try { uiPlugin.OnNotchStateChanged(state); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Plugin state notification error ({uiPlugin.Name}): {ex.Message}");
            }
        }
    }

    private void OpenPluginManager()
    {
        OpenPluginManager(null);
    }

    private void OpenPluginManager(string? focusPluginId)
    {
        if (_pluginManager == null || _pluginLibraryService == null) return;

        var pluginView = new PluginManagerView(_pluginManager, _pluginLibraryService, focusPluginId);
        var window = new Window
        {
            Title = "WinNotch Plugin Manager",
            Width = 980,
            Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Brushes.Transparent,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Content = pluginView,
            Topmost = true
        };
        window.Show();
        window.Activate();
        window.Topmost = false;
    }
}
