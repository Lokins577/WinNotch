using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WinNotch.WindowManagement;
using Key = System.Windows.Input.Key;
using Keyboard = System.Windows.Input.Keyboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace WinNotch.Views;

public partial class WindowSwitcherWindow : Window
{
    private readonly IWindowManagementBackend _backend;
    private readonly ObservableCollection<ManagedWindow> _windows = [];
    private readonly DispatcherTimer _refreshTimer;
    private IReadOnlyList<ManagedWindow> _allWindows = [];

    public WindowSwitcherWindow(IWindowManagementBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        InitializeComponent();
        WindowList.ItemsSource = _windows;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => RefreshWindows(preserveSelection: true);
        Loaded += (_, _) =>
        {
            RefreshWindows(preserveSelection: false);
            _refreshTimer.Start();
            SearchBox.Focus();
        };
    }

    private ManagedWindow? Selected => WindowList.SelectedItem as ManagedWindow;

    private void RefreshWindows(bool preserveSelection)
    {
        long selectedHandle = preserveSelection ? Selected?.Handle ?? 0 : 0;
        try
        {
            _allWindows = _backend.GetWindows(CurrentDesktopOnlyCheck.IsChecked == true);
            ApplySearch(selectedHandle);
            StatusText.Text = _windows.Count == 0
                ? "No matching top-level windows are available."
                : $"{_windows.Count} window{(_windows.Count == 1 ? string.Empty : "s")} · live DWM preview";
        }
        catch (Exception exception)
        {
            _allWindows = [];
            _windows.Clear();
            PreviewHost.SourceHandle = 0;
            StatusText.Text = exception.Message;
        }
    }

    private void ApplySearch(long selectedHandle = 0)
    {
        string query = SearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allWindows
            : _allWindows.Where(window =>
                    window.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || window.ProcessName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToArray();
        _windows.Clear();
        foreach (var window in filtered) _windows.Add(window);
        WindowList.SelectedItem = _windows.FirstOrDefault(window => window.Handle == selectedHandle)
                                  ?? _windows.FirstOrDefault();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplySearch(Selected?.Handle ?? 0);
    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized) RefreshWindows(preserveSelection: true);
    }

    private void OnWindowSelected(object sender, SelectionChangedEventArgs e)
    {
        PreviewHost.SourceHandle = Selected?.Handle ?? 0;
    }

    private void OnApplyLayout(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }
            || !Enum.TryParse<WindowLayoutPreset>(name, out var preset)) return;
        Run(window => _backend.ApplyLayout(window.Handle, preset), refresh: true);
    }

    private void OnMoveMonitor(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string directionText }
            || !int.TryParse(directionText, out int direction)) return;
        Run(window => _backend.MoveToAdjacentMonitor(window.Handle, direction), refresh: true);
    }

    private void OnRestore(object sender, RoutedEventArgs e)
        => Run(window => _backend.Restore(window.Handle), refresh: true);

    private void OnActivate(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        var result = _backend.Activate(Selected.Handle);
        if (result.Success) Close();
        else StatusText.Text = result.Message;
    }

    private async void OnCloseWindow(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        var result = _backend.RequestClose(Selected.Handle);
        StatusText.Text = result.Message;
        if (result.Success)
        {
            await Task.Delay(250);
            RefreshWindows(preserveSelection: false);
        }
    }

    private async void OnQuitApplication(object sender, RoutedEventArgs e)
    {
        var selected = Selected;
        if (selected == null) return;
        if (MessageBox.Show(
                $"Ask visible {selected.ProcessName} windows to close?\n\nThe app remains responsible for save prompts. WinNotch will not force-stop the process.",
                "Quit application",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var result = _backend.RequestQuit(selected.ProcessId);
        StatusText.Text = result.Message;
        if (result.Success)
        {
            await Task.Delay(300);
            RefreshWindows(preserveSelection: false);
        }
    }

    private void Run(
        Func<ManagedWindow, WindowOperationResult> action,
        bool refresh)
    {
        var selected = Selected;
        if (selected == null) return;
        var result = action(selected);
        StatusText.Text = result.Message;
        if (result.Success && refresh) RefreshWindows(preserveSelection: true);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && Keyboard.FocusedElement is not Button)
        {
            WindowList.SelectedIndex = Math.Min(WindowList.SelectedIndex + 1, _windows.Count - 1);
            WindowList.ScrollIntoView(WindowList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && Keyboard.FocusedElement is not Button)
        {
            WindowList.SelectedIndex = Math.Max(WindowList.SelectedIndex - 1, 0);
            WindowList.ScrollIntoView(WindowList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button)
        {
            OnActivate(this, e);
            e.Handled = true;
        }
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        PreviewHost.SourceHandle = 0;
        base.OnClosed(e);
    }
}
