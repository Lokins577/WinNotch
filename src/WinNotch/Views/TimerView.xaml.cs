using System.Windows;
using System.Windows.Controls;
using WinNotch.Services;
using WinNotch.Timers;

namespace WinNotch.Views;

public partial class TimerView : UserControl
{
    private TimerFeatureService? _service;
    private bool _compact;

    public TimerView()
    {
        InitializeComponent();
    }

    public void Bind(TimerFeatureService service, bool compact = false)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_service != null)
            _service.Updated -= OnUpdated;

        _service = service;
        _service.Updated += OnUpdated;
        _compact = compact;
        if (compact)
        {
            RootBorder.Padding = new Thickness(7);
            TimeText.FontSize = 20;
            SessionText.Visibility = Visibility.Collapsed;
        }
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
        PhaseText.Text = snapshot.Phase switch
        {
            TimerPhase.Focus => "Focus",
            TimerPhase.ShortBreak => "Short break",
            TimerPhase.LongBreak => "Long break",
            TimerPhase.Countdown => "Countdown",
            TimerPhase.Stopwatch => "Stopwatch",
            _ => "Timer"
        };
        TimeText.Text = Format(snapshot.Value);
        SessionText.Text = snapshot.CompletedFocusToday == 1
            ? "1 focus today"
            : $"{snapshot.CompletedFocusToday} focus today";
        StartPauseButton.Content = snapshot.State == TimerRunState.Running ? "Pause" : "Start";
        CountdownAdjustments.Visibility = snapshot.Phase == TimerPhase.Countdown
            && snapshot.State != TimerRunState.Running
            ? Visibility.Visible
            : Visibility.Collapsed;
        SessionText.Visibility = _compact || snapshot.Phase == TimerPhase.Countdown
            ? Visibility.Collapsed
            : Visibility.Visible;
        SkipButton.Visibility = snapshot.Phase is TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak
            ? Visibility.Visible
            : Visibility.Collapsed;
        FocusModeButton.FontWeight = snapshot.Phase is TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak
            ? FontWeights.SemiBold
            : FontWeights.Normal;
        CountdownModeButton.FontWeight = snapshot.Phase == TimerPhase.Countdown
            ? FontWeights.SemiBold
            : FontWeights.Normal;
        StopwatchModeButton.FontWeight = snapshot.Phase == TimerPhase.Stopwatch
            ? FontWeights.SemiBold
            : FontWeights.Normal;
    }

    private static string Format(TimeSpan value)
    {
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
        return $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";
    }

    private void OnFocusMode(object sender, RoutedEventArgs e) => _service?.SelectFocus();
    private void OnCountdownMode(object sender, RoutedEventArgs e) => _service?.SelectCountdown();
    private void OnStopwatchMode(object sender, RoutedEventArgs e) => _service?.SelectStopwatch();
    private void OnStartPause(object sender, RoutedEventArgs e) => _service?.Toggle();
    private void OnReset(object sender, RoutedEventArgs e) => _service?.Reset();
    private void OnSkip(object sender, RoutedEventArgs e) => _service?.Skip();
    private void OnSubtractMinute(object sender, RoutedEventArgs e) => _service?.AdjustCountdown(-1);
    private void OnAddMinute(object sender, RoutedEventArgs e) => _service?.AdjustCountdown(1);
}
