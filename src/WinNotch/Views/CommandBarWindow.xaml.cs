using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using WinNotch.CommandBar;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace WinNotch.Views;

public partial class CommandBarWindow : Window
{
    private readonly CommandBarEngine _engine;
    private readonly ObservableCollection<CommandResult> _results = [];
    private CancellationTokenSource? _searchCancellation;
    private bool _executing;

    public CommandBarWindow(CommandBarEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        InitializeComponent();
        ResultsList.ItemsSource = _results;
        Loaded += async (_, _) =>
        {
            QueryBox.Focus();
            await SearchAsync();
        };
    }

    private async void OnQueryChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;
        try
        {
            await Task.Delay(70, token);
            var results = await _engine.SearchAsync(QueryBox.Text, cancellationToken: token);
            if (token.IsCancellationRequested) return;
            _results.Clear();
            foreach (var result in results) _results.Add(result);
            ResultsList.SelectedIndex = _results.Count > 0 ? 0 : -1;
        }
        catch (OperationCanceledException) { }
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            ResultsList.SelectedIndex = Math.Min(ResultsList.SelectedIndex + 1, _results.Count - 1);
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            ResultsList.SelectedIndex = Math.Max(ResultsList.SelectedIndex - 1, 0);
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            await ExecuteSelectedAsync();
            e.Handled = true;
        }
    }

    private async void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
        => await ExecuteSelectedAsync();

    private async Task ExecuteSelectedAsync()
    {
        if (_executing || ResultsList.SelectedItem is not CommandResult result || result.Execute == null)
            return;
        _executing = true;
        try
        {
            await result.Execute(CancellationToken.None);
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Command failed", MessageBoxButton.OK, MessageBoxImage.Error);
            _executing = false;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_executing) Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        base.OnClosed(e);
    }
}
