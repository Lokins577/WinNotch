using System.Diagnostics;
using WinNotch.Helpers;
using WinNotch.QuickTools;
using WinNotch.Services;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private RadialMenuFeatureService? _subscribedRadialMenu;
    private RadialMenuWindow? _radialMenuWindow;

    private void AttachRadialMenu()
    {
        if (!_isPrimary || _services.RadialMenu == null
            || ReferenceEquals(_subscribedRadialMenu, _services.RadialMenu)) return;
        if (_subscribedRadialMenu != null)
            _subscribedRadialMenu.OpenRequested -= OnRadialMenuRequested;
        _subscribedRadialMenu = _services.RadialMenu;
        _subscribedRadialMenu.OpenRequested += OnRadialMenuRequested;
    }

    private void DetachRadialMenu()
    {
        if (_subscribedRadialMenu != null)
            _subscribedRadialMenu.OpenRequested -= OnRadialMenuRequested;
        _subscribedRadialMenu = null;
        _radialMenuWindow?.Close();
        _radialMenuWindow = null;
    }

    private void OnRadialMenuRequested() => Dispatcher.Invoke(OpenRadialMenu);

    private void OpenRadialMenu()
    {
        if (_radialMenuWindow is { IsVisible: true })
        {
            _radialMenuWindow.Activate();
            return;
        }
        _settings.RadialMenu.Normalize();
        _radialMenuWindow = new RadialMenuWindow(_settings.RadialMenu.Items, ExecuteRadialActionAsync)
        {
            Owner = this
        };
        _radialMenuWindow.Closed += (_, _) => _radialMenuWindow = null;
        _radialMenuWindow.Show();
        _radialMenuWindow.Activate();
    }

    private Task ExecuteRadialActionAsync(RadialMenuItem item)
    {
        switch (item.Kind)
        {
            case RadialActionKind.CommandBar: OpenCommandBar(); break;
            case RadialActionKind.Scratchpad: OpenScratchpad(); break;
            case RadialActionKind.Features: OpenFeatureCatalog(); break;
            case RadialActionKind.WindowSwitcher: OpenWindowSwitcher(); break;
            case RadialActionKind.SystemMonitor: OpenSystemMonitor(); break;
            case RadialActionKind.AudioMixer: OpenAudioMixer(); break;
            case RadialActionKind.Capture: OpenCaptureTool(); break;
            case RadialActionKind.MediaTools: OpenMediaTools(); break;
            case RadialActionKind.AppManagement: OpenAppManagement(); break;
            case RadialActionKind.InputSettings: OpenInputSettings(); break;
            case RadialActionKind.ToggleMute: _volumeService.ToggleMute(); break;
            case RadialActionKind.ToggleTheme: ToggleQuickTheme(); break;
            case RadialActionKind.Lock: SystemActions.LockWorkstation(); break;
            case RadialActionKind.Application:
            case RadialActionKind.File:
            case RadialActionKind.Url:
                if (!RadialActionTarget.TryResolve(item.Kind, item.Target, out var target, out var error))
                    throw new InvalidOperationException(error);
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                break;
        }
        return Task.CompletedTask;
    }

    private void OpenRadialMenuSettings()
    {
        var window = new RadialMenuSettingsWindow(_settings, _settingsStore)
        {
            Owner = this
        };
        window.Show();
        window.Activate();
    }
}
