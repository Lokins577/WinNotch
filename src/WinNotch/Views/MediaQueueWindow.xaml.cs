using System.Windows;
using WinNotch.Music;
using WinNotch.Services;

namespace WinNotch.Views;

public partial class MediaQueueWindow : Window
{
    private readonly MediaQueueService _service;

    public MediaQueueWindow(MediaQueueService service)
    {
        _service = service;
        InitializeComponent();
        _service.Updated += OnUpdated;
        Closed += (_, _) => _service.Updated -= OnUpdated;
        Render(_service.Snapshot);
        _ = _service.RefreshAsync();
    }

    private void OnUpdated(MediaQueueSnapshot snapshot) =>
        Dispatcher.BeginInvoke(() => Render(snapshot));

    private void Render(MediaQueueSnapshot snapshot)
    {
        ProviderText.Text = snapshot.Provider == null
            ? "Playback queue"
            : $"{snapshot.Provider} queue";
        StatusText.Text = snapshot.Message ?? snapshot.Capability switch
        {
            MediaQueueCapability.Available => $"{snapshot.Queue.Count} item(s)",
            _ => snapshot.Capability.ToString()
        };
        QueueList.ItemsSource = snapshot.Queue;
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) =>
        await _service.RefreshAsync();
}
