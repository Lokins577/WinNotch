using System.Windows;
using System.Windows.Controls;
using WinNotch.Notifications;
using WinNotch.Services;

namespace WinNotch.Views;

public partial class NotificationCompactView : UserControl
{
    private NotificationListenerService? _service;

    public NotificationCompactView()
    {
        InitializeComponent();
    }

    public void Bind(NotificationListenerService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_service != null)
            _service.Updated -= OnUpdated;
        _service = service;
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
        var latest = snapshot.Notifications.FirstOrDefault();
        Visibility = latest == null ? Visibility.Collapsed : Visibility.Visible;
        NotificationText.Text = latest == null ? string.Empty : $"{latest.AppName}: {latest.Title}";
    }
}
