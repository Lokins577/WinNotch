using WinNotch.Services;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private CommandBarWindow? _commandBarWindow;
    private CommandBarFeatureService? _subscribedCommandBar;

    private void AttachCommandBar()
    {
        if (!_isPrimary || _services.CommandBar == null
            || ReferenceEquals(_subscribedCommandBar, _services.CommandBar))
            return;
        if (_subscribedCommandBar != null)
            _subscribedCommandBar.OpenRequested -= OnCommandBarRequested;
        _subscribedCommandBar = _services.CommandBar;
        _subscribedCommandBar.OpenRequested += OnCommandBarRequested;
    }

    private void DetachCommandBar()
    {
        if (_subscribedCommandBar != null)
            _subscribedCommandBar.OpenRequested -= OnCommandBarRequested;
        _subscribedCommandBar = null;
        _commandBarWindow?.Close();
        _commandBarWindow = null;
    }

    private void OnCommandBarRequested()
    {
        Dispatcher.Invoke(OpenCommandBar);
    }

    private void OpenCommandBar()
    {
        if (_services.CommandBar == null) return;
        if (_commandBarWindow is { IsVisible: true })
        {
            _commandBarWindow.Activate();
            return;
        }

        _commandBarWindow = new CommandBarWindow(_services.CommandBar.Engine)
        {
            Owner = this
        };
        _commandBarWindow.Closed += (_, _) => _commandBarWindow = null;
        _commandBarWindow.Show();
        _commandBarWindow.Activate();
    }

    private void OpenCommandBarSettings()
    {
        var window = new CommandBarSettingsWindow(
            _settings,
            _settingsStore,
            () => _services.CommandBar?.InvalidateFileIndex())
        {
            Owner = this
        };
        window.Show();
        window.Activate();
    }
}
