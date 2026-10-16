using WinNotch.Timers;
using Xunit;

namespace WinNotch.Tests;

public sealed class TimerEngineTests
{
    [Fact]
    public void Countdown_UsesElapsedTimeAndSurvivesPauseResume()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero));
        var preferences = new TimerPreferences { CountdownMinutes = 10 };
        var engine = new TimerEngine(preferences, new TimerSession(), clock);
        engine.SelectCountdown();
        engine.Toggle();

        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(TimeSpan.FromMinutes(7), engine.Snapshot.Value);

        engine.Toggle();
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(7), engine.Snapshot.Value);

        engine.Toggle();
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(5), engine.Snapshot.Value);
    }

    [Fact]
    public void FocusCycles_ChooseLongBreakAtConfiguredInterval()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero));
        var preferences = new TimerPreferences
        {
            FocusMinutes = 1,
            ShortBreakMinutes = 1,
            LongBreakMinutes = 2,
            LongBreakEvery = 2
        };
        var engine = new TimerEngine(preferences, new TimerSession(), clock);

        engine.Toggle();
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(engine.Refresh());
        Assert.Equal(TimerPhase.ShortBreak, engine.Snapshot.Phase);
        Assert.Equal(1, engine.Snapshot.CompletedFocusToday);

        engine.Skip();
        engine.Toggle();
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(engine.Refresh());
        Assert.Equal(TimerPhase.LongBreak, engine.Snapshot.Phase);
        Assert.Equal(2, engine.Snapshot.CompletedFocusToday);
        Assert.Equal(0, engine.Snapshot.FocusSessionsSinceLongBreak);
    }

    [Fact]
    public void RestoredRunningTimer_CompletesFromUtcAnchor()
    {
        var start = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(start.AddMinutes(30));
        var session = new TimerSession
        {
            Initialized = true,
            Phase = TimerPhase.Focus,
            State = TimerRunState.Running,
            StoredValue = TimeSpan.FromMinutes(25),
            AnchorUtc = start,
            ActivityDate = DateOnly.FromDateTime(start.DateTime)
        };
        var engine = new TimerEngine(new TimerPreferences(), session, clock);
        TimerCompletion? completion = null;
        engine.Completed += value => completion = value;

        Assert.True(engine.Refresh());
        Assert.NotNull(completion);
        Assert.Equal(TimerPhase.Focus, completion!.CompletedPhase);
        Assert.Equal(TimerPhase.ShortBreak, engine.Snapshot.Phase);
        Assert.Equal(TimerRunState.Ready, engine.Snapshot.State);
    }

    [Fact]
    public void Stopwatch_CountsUpAndPauses()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero));
        var engine = new TimerEngine(new TimerPreferences(), new TimerSession(), clock);
        engine.SelectStopwatch();
        engine.Toggle();

        clock.Advance(TimeSpan.FromSeconds(75));
        Assert.Equal(TimeSpan.FromSeconds(75), engine.Snapshot.Value);

        engine.Toggle();
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromSeconds(75), engine.Snapshot.Value);
        Assert.Equal(TimerRunState.Paused, engine.Snapshot.State);
    }

    [Fact]
    public void Snapshot_ResetsDailyCompletionCount()
    {
        var start = new DateTimeOffset(2026, 10, 17, 23, 59, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(start);
        var session = new TimerSession
        {
            ActivityDate = DateOnly.FromDateTime(start.DateTime),
            CompletedFocusToday = 4
        };
        var engine = new TimerEngine(new TimerPreferences(), session, clock);

        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(0, engine.Snapshot.CompletedFocusToday);
        Assert.Equal(DateOnly.FromDateTime(clock.GetUtcNow().DateTime), session.ActivityDate);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
