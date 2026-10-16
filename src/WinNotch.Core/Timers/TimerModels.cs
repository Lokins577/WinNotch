namespace WinNotch.Timers;

public enum TimerPhase
{
    Focus,
    ShortBreak,
    LongBreak,
    Countdown,
    Stopwatch
}

public enum TimerRunState
{
    Ready,
    Running,
    Paused
}

public sealed class TimerPreferences
{
    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int LongBreakEvery { get; set; } = 4;
    public int CountdownMinutes { get; set; } = 10;

    public void Normalize()
    {
        FocusMinutes = Math.Clamp(FocusMinutes, 1, 180);
        ShortBreakMinutes = Math.Clamp(ShortBreakMinutes, 1, 60);
        LongBreakMinutes = Math.Clamp(LongBreakMinutes, 1, 120);
        LongBreakEvery = Math.Clamp(LongBreakEvery, 1, 12);
        CountdownMinutes = Math.Clamp(CountdownMinutes, 1, 720);
    }
}

public sealed class TimerSession
{
    public bool Initialized { get; set; }
    public TimerPhase Phase { get; set; } = TimerPhase.Focus;
    public TimerRunState State { get; set; } = TimerRunState.Ready;
    public TimeSpan StoredValue { get; set; }
    public DateTimeOffset? AnchorUtc { get; set; }
    public DateOnly ActivityDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int CompletedFocusToday { get; set; }
    public int FocusSessionsSinceLongBreak { get; set; }
}

public sealed record TimerSnapshot(
    TimerPhase Phase,
    TimerRunState State,
    TimeSpan Value,
    int CompletedFocusToday,
    int FocusSessionsSinceLongBreak)
{
    public bool IsStopwatch => Phase == TimerPhase.Stopwatch;
}

public sealed record TimerCompletion(TimerPhase CompletedPhase, TimerPhase NextPhase);
