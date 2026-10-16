using System.Windows;
using System.Windows.Controls;
using WinNotch.Services;
using WinNotch.Timers;

namespace WinNotch.Views;

public partial class TimerCompactView : UserControl
{
    private TimerFeatureService? _service;

    public TimerCompactView()
    {
        InitializeComponent();
    }

    public void Bind(TimerFeatureService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_service != null)
            _service.Updated -= OnUpdated;
        _service = service;
        _service.Updated += OnUpdated;
        Render(service.Snapshot);
    }

    private void OnUpdated(TimerSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Render(snapshot));
            return;
        }
        Render(snapshot);
    }

    private void Render(TimerSnapshot snapshot)
    {
        Visibility = snapshot.State == TimerRunState.Ready
            ? Visibility.Collapsed
            : Visibility.Visible;
        TimeText.Text = snapshot.Value.TotalHours >= 1
            ? $"{(int)snapshot.Value.TotalHours:00}:{snapshot.Value.Minutes:00}:{snapshot.Value.Seconds:00}"
            : $"{(int)snapshot.Value.TotalMinutes:00}:{snapshot.Value.Seconds:00}";
        StatusDot.Opacity = snapshot.State == TimerRunState.Running ? 1.0 : 0.45;
    }
}
