namespace WinNotch.Timers;

public sealed class TimerEngine
{
    private readonly TimeProvider _timeProvider;

    public TimerEngine(
        TimerPreferences preferences,
        TimerSession session,
        TimeProvider? timeProvider = null)
    {
        Preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _timeProvider = timeProvider ?? TimeProvider.System;

        Preferences.Normalize();
        NormalizeSession();
    }

    public TimerPreferences Preferences { get; }
    public TimerSession Session { get; }

    public event Action<TimerCompletion>? Completed;

    public TimerSnapshot Snapshot
    {
        get
        {
            ResetDailyCountIfNeeded();
            return new TimerSnapshot(
                Session.Phase,
                Session.State,
                GetCurrentValue(),
                Session.CompletedFocusToday,
                Session.FocusSessionsSinceLongBreak);
        }
    }

    public bool Refresh()
    {
        ResetDailyCountIfNeeded();
        if (Session.State != TimerRunState.Running || Session.Phase == TimerPhase.Stopwatch)
            return false;
        if (GetCurrentValue() > TimeSpan.Zero)
            return false;

        CompleteCurrentPhase();
        return true;
    }

    public void Toggle()
    {
        ResetDailyCountIfNeeded();
        if (Session.State == TimerRunState.Running)
        {
            Session.StoredValue = GetCurrentValue();
            Session.AnchorUtc = null;
            Session.State = TimerRunState.Paused;
            return;
        }

        if (Session.Phase != TimerPhase.Stopwatch && Session.StoredValue <= TimeSpan.Zero)
            Session.StoredValue = GetPhaseDuration(Session.Phase);

        Session.AnchorUtc = _timeProvider.GetUtcNow();
        Session.State = TimerRunState.Running;
    }

    public void Reset()
    {
        Session.State = TimerRunState.Ready;
        Session.AnchorUtc = null;
        Session.StoredValue = Session.Phase == TimerPhase.Stopwatch
            ? TimeSpan.Zero
            : GetPhaseDuration(Session.Phase);
    }

    public void Skip()
    {
        if (Session.Phase is TimerPhase.Countdown or TimerPhase.Stopwatch)
        {
            Reset();
            return;
        }

        AdvanceFocusPhase(completedNaturally: false);
    }

    public void SelectFocus()
    {
        SelectPhase(TimerPhase.Focus);
    }

    public void SelectCountdown(TimeSpan? duration = null)
    {
        if (duration is { } requested)
            Preferences.CountdownMinutes = Math.Clamp((int)Math.Round(requested.TotalMinutes), 1, 720);
        Preferences.Normalize();
        SelectPhase(TimerPhase.Countdown);
    }

    public void SelectStopwatch()
    {
        SelectPhase(TimerPhase.Stopwatch);
    }

    public TimeSpan GetPhaseDuration(TimerPhase phase)
    {
        Preferences.Normalize();
        return phase switch
        {
            TimerPhase.Focus => TimeSpan.FromMinutes(Preferences.FocusMinutes),
            TimerPhase.ShortBreak => TimeSpan.FromMinutes(Preferences.ShortBreakMinutes),
            TimerPhase.LongBreak => TimeSpan.FromMinutes(Preferences.LongBreakMinutes),
            TimerPhase.Countdown => TimeSpan.FromMinutes(Preferences.CountdownMinutes),
            TimerPhase.Stopwatch => TimeSpan.Zero,
            _ => TimeSpan.Zero
        };
    }

    private void SelectPhase(TimerPhase phase)
    {
        Session.Phase = phase;
        Session.State = TimerRunState.Ready;
        Session.AnchorUtc = null;
        Session.StoredValue = GetPhaseDuration(phase);
    }

    private TimeSpan GetCurrentValue()
    {
        if (Session.State != TimerRunState.Running || Session.AnchorUtc == null)
            return NonNegative(Session.StoredValue);

        var elapsed = _timeProvider.GetUtcNow() - Session.AnchorUtc.Value;
        return Session.Phase == TimerPhase.Stopwatch
            ? NonNegative(Session.StoredValue + elapsed)
            : NonNegative(Session.StoredValue - elapsed);
    }

    private void CompleteCurrentPhase()
    {
        var completedPhase = Session.Phase;
        if (completedPhase is TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak)
            AdvanceFocusPhase(completedNaturally: true);
        else
        {
            Session.State = TimerRunState.Ready;
            Session.AnchorUtc = null;
            Session.StoredValue = TimeSpan.Zero;
        }

        Completed?.Invoke(new TimerCompletion(completedPhase, Session.Phase));
    }

    private void AdvanceFocusPhase(bool completedNaturally)
    {
        ResetDailyCountIfNeeded();
        if (Session.Phase == TimerPhase.Focus)
        {
            if (completedNaturally)
            {
                Session.CompletedFocusToday++;
                Session.FocusSessionsSinceLongBreak++;
            }

            bool longBreak = completedNaturally
                && Session.FocusSessionsSinceLongBreak >= Preferences.LongBreakEvery;
            Session.Phase = longBreak ? TimerPhase.LongBreak : TimerPhase.ShortBreak;
            if (longBreak)
                Session.FocusSessionsSinceLongBreak = 0;
        }
        else
        {
            Session.Phase = TimerPhase.Focus;
        }

        Session.State = TimerRunState.Ready;
        Session.AnchorUtc = null;
        Session.StoredValue = GetPhaseDuration(Session.Phase);
    }

    private void NormalizeSession()
    {
        if (!Session.Initialized)
        {
            Session.Initialized = true;
            Session.Phase = TimerPhase.Focus;
            Session.State = TimerRunState.Ready;
            Session.AnchorUtc = null;
            Session.StoredValue = GetPhaseDuration(TimerPhase.Focus);
        }

        Session.CompletedFocusToday = Math.Max(0, Session.CompletedFocusToday);
        Session.FocusSessionsSinceLongBreak = Math.Max(0, Session.FocusSessionsSinceLongBreak);

        if (!Enum.IsDefined(Session.Phase)) Session.Phase = TimerPhase.Focus;
        if (!Enum.IsDefined(Session.State)) Session.State = TimerRunState.Ready;

        if (Session.State == TimerRunState.Running && Session.AnchorUtc == null)
            Session.State = TimerRunState.Paused;
        if (Session.StoredValue < TimeSpan.Zero)
            Session.StoredValue = TimeSpan.Zero;
        if (Session.State == TimerRunState.Ready && Session.StoredValue == TimeSpan.Zero
            && Session.Phase != TimerPhase.Stopwatch)
            Session.StoredValue = GetPhaseDuration(Session.Phase);

        ResetDailyCountIfNeeded();
    }

    private void ResetDailyCountIfNeeded()
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        if (Session.ActivityDate == today) return;

        Session.ActivityDate = today;
        Session.CompletedFocusToday = 0;
    }

    private static TimeSpan NonNegative(TimeSpan value)
    {
        return value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }
}
