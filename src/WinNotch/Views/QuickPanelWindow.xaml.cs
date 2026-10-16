using System.Windows;
using System.Windows.Controls;

namespace WinNotch.Views;

public sealed record QuickPanelActions(
    Action OpenCommandBar,
    Action OpenScratchpad,
    Action OpenFeatures,
    Action ToggleMute,
    Action ToggleTheme,
    Action Lock);

public partial class QuickPanelWindow : Window
{
    private readonly QuickPanelActions _actions;
    private bool _acting;

    public QuickPanelWindow(QuickPanelActions actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        InitializeComponent();
    }

    private void OnAction(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        _acting = true;
        try
        {
            switch (action)
            {
                case "command": _actions.OpenCommandBar(); break;
                case "scratchpad": _actions.OpenScratchpad(); break;
                case "features": _actions.OpenFeatures(); break;
                case "mute": _actions.ToggleMute(); break;
                case "theme": _actions.ToggleTheme(); break;
                case "lock": _actions.Lock(); break;
            }
            Close();
        }
        finally { _acting = false; }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_acting) Close();
    }
}
