using System.Windows;
using System.Windows.Controls;
using WinNotch.Downloads;
using WinNotch.Services;

namespace WinNotch.Views;

public partial class DownloadCompactView : UserControl
{
    private DownloadMonitorService? _service;

    public DownloadCompactView()
    {
        InitializeComponent();
    }

    public void Bind(DownloadMonitorService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_service != null)
            _service.Updated -= OnUpdated;
        _service = service;
        _service.Updated += OnUpdated;
        Render(service.Snapshot);
    }

    private void OnUpdated(DownloadMonitorSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Render(snapshot));
            return;
        }
        Render(snapshot);
    }

    private void Render(DownloadMonitorSnapshot snapshot)
    {
        var current = snapshot.Activities.FirstOrDefault();
        Visibility = current == null ? Visibility.Collapsed : Visibility.Visible;
        ActivityText.Text = current?.DisplayName ?? string.Empty;
    }
}
