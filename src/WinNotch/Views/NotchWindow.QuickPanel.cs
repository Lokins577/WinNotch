using System.Windows;
using WinNotch.Features;
using WinNotch.Helpers;
using WinNotch.Models;
using WinNotch.Services;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private QuickPanelWindow? _quickPanelWindow;
    private ScratchpadWindow? _scratchpadWindow;
    private QuickPanelFeatureService? _subscribedQuickPanel;

    private void AttachQuickPanel()
    {
        if (!_isPrimary || _services.QuickPanel == null
            || ReferenceEquals(_subscribedQuickPanel, _services.QuickPanel)) return;
        if (_subscribedQuickPanel != null)
            _subscribedQuickPanel.OpenRequested -= OnQuickPanelRequested;
        _subscribedQuickPanel = _services.QuickPanel;
        _subscribedQuickPanel.OpenRequested += OnQuickPanelRequested;
    }

    private void DetachQuickPanel()
    {
        if (_subscribedQuickPanel != null)
            _subscribedQuickPanel.OpenRequested -= OnQuickPanelRequested;
        _subscribedQuickPanel = null;
        _quickPanelWindow?.Close();
        _quickPanelWindow = null;
    }

    private void OnQuickPanelRequested() => Dispatcher.Invoke(OpenQuickPanel);

    private void OpenQuickPanel()
    {
        if (_quickPanelWindow is { IsVisible: true })
        {
            _quickPanelWindow.Activate();
            return;
        }
        _quickPanelWindow = new QuickPanelWindow(new QuickPanelActions(
            OpenCommandBar,
            OpenScratchpad,
            OpenFeatureCatalog,
            _volumeService.ToggleMute,
            ToggleQuickTheme,
            () => SystemActions.LockWorkstation()))
        {
            Owner = this
        };
        _quickPanelWindow.Closed += (_, _) => _quickPanelWindow = null;
        _quickPanelWindow.Show();
        _quickPanelWindow.Activate();
    }

    private async void OpenScratchpad()
    {
        if (_services.Scratchpad == null)
        {
            await _services.FeatureRuntime.SetEnabledAsync(BuiltInFeatures.Scratchpad.Id, true);
            _settings.SetFeatureAvailable(BuiltInFeatures.Scratchpad, true);
            _settingsStore.Save(_settings);
        }
        if (_services.Scratchpad == null) return;
        if (_scratchpadWindow is { IsVisible: true })
        {
            _scratchpadWindow.Activate();
            return;
        }
        _scratchpadWindow = new ScratchpadWindow(_services.Scratchpad) { Owner = this };
        _scratchpadWindow.Closed += (_, _) => _scratchpadWindow = null;
        _scratchpadWindow.Show();
        _scratchpadWindow.Activate();
    }

    private void ToggleQuickTheme()
    {
        _settings.AdaptToWindowsTheme = false;
        _settings.Theme = _themeService.IsLight ? AppTheme.Dark : AppTheme.Light;
        _settingsStore.Save(_settings);
        ApplySettings(_settings);
        SettingsUpdatedByUser?.Invoke(_settings);
    }
}
