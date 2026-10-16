using System.Windows;
using System.Windows.Controls;
using WinNotch.Downloads;
using WinNotch.Services;
using Brush = System.Windows.Media.Brush;

namespace WinNotch.Views;

public partial class DownloadActivityView : UserControl
{
    private DownloadMonitorService? _service;
    private bool _compact;

    public DownloadActivityView()
    {
        InitializeComponent();
    }

    public void Bind(DownloadMonitorService service, bool compact = false)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_service != null)
            _service.Updated -= OnUpdated;
        _service = service;
        _compact = compact;
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
        StatusText.Text = snapshot.Status switch
        {
            DownloadMonitorStatus.Watching => "Watching",
            DownloadMonitorStatus.NeedsFolder => "Setup needed",
            DownloadMonitorStatus.Faulted => "Unavailable",
            _ => "Off"
        };
        EmptyText.Text = snapshot.Status switch
        {
            DownloadMonitorStatus.NeedsFolder => "Choose a download folder in Settings.",
            DownloadMonitorStatus.Faulted => snapshot.Error ?? "The folder could not be monitored.",
            _ when snapshot.Activities.Count == 0 => "No active downloads",
            _ => string.Empty
        };
        EmptyText.Visibility = string.IsNullOrEmpty(EmptyText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        ActivityHost.Children.Clear();
        foreach (var activity in snapshot.Activities.Take(_compact ? 2 : 5))
            ActivityHost.Children.Add(CreateRow(activity));
    }

    private static FrameworkElement CreateRow(DownloadActivity activity)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock
        {
            Text = activity.DisplayName,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 10,
            Foreground = Application.Current?.TryFindResource("TextSecondaryBrush") as Brush
                ?? Brushes.LightGray
        };
        var state = new TextBlock
        {
            Text = activity.State == DownloadActivityState.Completed
                ? "Done"
                : FormatBytes(activity.Bytes),
            FontSize = 9,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = Application.Current?.TryFindResource("TextDimBrush") as Brush
                ?? Brushes.Gray
        };
        grid.Children.Add(name);
        Grid.SetColumn(state, 1);
        grid.Children.Add(state);
        return grid;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "Starting";
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024 * 1024):0.0} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024d * 1024):0.0} MB";
        if (bytes >= 1024L) return $"{bytes / 1024d:0.0} KB";
        return $"{bytes} B";
    }
}
