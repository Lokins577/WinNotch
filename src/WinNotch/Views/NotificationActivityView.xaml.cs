using System.Windows;
using System.Windows.Controls;
using WinNotch.Notifications;
using WinNotch.Services;
using Brush = System.Windows.Media.Brush;

namespace WinNotch.Views;

public partial class NotificationActivityView : UserControl
{
    private NotificationListenerService? _service;
    private bool _compact;

    public NotificationActivityView()
    {
        InitializeComponent();
    }

    public void Bind(NotificationListenerService service, bool compact = false)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_service != null)
            _service.Updated -= OnUpdated;
        _service = service;
        _compact = compact;
        _service.Updated += OnUpdated;
        Render(service.Snapshot);
    }

    private void OnUpdated(NotificationInboxSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Render(snapshot));
            return;
        }
        Render(snapshot);
    }

    private void Render(NotificationInboxSnapshot snapshot)
    {
        AccessButton.Visibility = snapshot.Access is NotificationAccessState.NotRequested
            or NotificationAccessState.Denied
            ? Visibility.Visible
            : Visibility.Collapsed;
        AccessButton.Content = snapshot.Access == NotificationAccessState.Denied
            ? "Check again"
            : "Allow";

        StatusText.Text = snapshot.Message ?? (snapshot.Notifications.Count == 0
            ? "No notifications"
            : string.Empty);
        StatusText.Visibility = string.IsNullOrEmpty(StatusText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        NotificationHost.Children.Clear();
        foreach (var notification in snapshot.Notifications.Take(_compact ? 2 : 5))
            NotificationHost.Children.Add(CreateRow(notification));
    }

    private FrameworkElement CreateRow(NotificationActivity notification)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = $"{notification.AppName} · {notification.Title}",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = GetBrush("TextPrimaryBrush", System.Windows.Media.Brushes.White)
        });
        if (!string.IsNullOrWhiteSpace(notification.Body))
        {
            text.Children.Add(new TextBlock
            {
                Text = notification.Body,
                FontSize = 9,
                MaxHeight = _compact ? 28 : 42,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = GetBrush("TextSecondaryBrush", System.Windows.Media.Brushes.LightGray)
            });
        }
        grid.Children.Add(text);

        var dismiss = new Button
        {
            Content = "×",
            Width = 24,
            Height = 24,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Dismiss notification"
        };
        dismiss.Click += async (_, _) =>
        {
            if (_service != null)
                await _service.DismissAsync(notification.Id);
        };
        Grid.SetColumn(dismiss, 1);
        grid.Children.Add(dismiss);
        return grid;
    }

    private async void OnRequestAccess(object sender, RoutedEventArgs e)
    {
        if (_service == null) return;
        AccessButton.IsEnabled = false;
        try { await _service.RequestAccessAsync(); }
        finally { AccessButton.IsEnabled = true; }
    }

    private static Brush GetBrush(string key, Brush fallback)
    {
        return Application.Current?.TryFindResource(key) as Brush ?? fallback;
    }
}
