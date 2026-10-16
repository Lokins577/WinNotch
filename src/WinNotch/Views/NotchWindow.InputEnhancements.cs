using WinNotch.Services;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private InputEnhancementFeatureService? _subscribedInputEnhancements;
    private InputEnhancementSettingsWindow? _inputSettingsWindow;

    private void AttachInputEnhancements()
    {
        if (!_isPrimary || _services.InputEnhancements == null
            || ReferenceEquals(_subscribedInputEnhancements, _services.InputEnhancements)) return;
        DetachInputEnhancementEvents();
        _subscribedInputEnhancements = _services.InputEnhancements;
        _subscribedInputEnhancements.OpenRequested += OnInputSettingsRequested;
        _subscribedInputEnhancements.CommandBarRequested += OnInputCommandBarRequested;
        _subscribedInputEnhancements.QuickPanelRequested += OnInputQuickPanelRequested;
    }

    private void DetachInputEnhancements()
    {
        DetachInputEnhancementEvents();
        _inputSettingsWindow?.Close();
        _inputSettingsWindow = null;
    }

    private void DetachInputEnhancementEvents()
    {
        if (_subscribedInputEnhancements != null)
        {
            _subscribedInputEnhancements.OpenRequested -= OnInputSettingsRequested;
            _subscribedInputEnhancements.CommandBarRequested -= OnInputCommandBarRequested;
            _subscribedInputEnhancements.QuickPanelRequested -= OnInputQuickPanelRequested;
        }
        _subscribedInputEnhancements = null;
    }

    private void OnInputSettingsRequested() => Dispatcher.BeginInvoke(OpenInputSettings);
    private void OnInputCommandBarRequested() => Dispatcher.BeginInvoke(OpenCommandBar);
    private void OnInputQuickPanelRequested() => Dispatcher.BeginInvoke(OpenQuickPanel);

    private void OpenInputSettings()
    {
        if (_services.InputEnhancements == null) return;
        if (_inputSettingsWindow is { IsVisible: true })
        {
            _inputSettingsWindow.Activate();
            return;
        }
        _inputSettingsWindow = new InputEnhancementSettingsWindow(
            _settings, _settingsStore, _services.InputEnhancements) { Owner = this };
        _inputSettingsWindow.Closed += (_, _) => _inputSettingsWindow = null;
        _inputSettingsWindow.Show();
        _inputSettingsWindow.Activate();
    }
}
