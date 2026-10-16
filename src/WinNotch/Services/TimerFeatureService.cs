using System.Media;
using System.Windows.Threading;
using WinNotch.Models;
using WinNotch.Features;
using WinNotch.Settings;
using WinNotch.Timers;

namespace WinNotch.Services;

public sealed class TimerFeatureService : IFeatureModule, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;
    private readonly TimerEngine _engine;
    private readonly DispatcherTimer _ticker;
    private bool _started;

    public TimerFeatureService(AppSettings settings, IAppSettingsStore settingsStore)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _engine = new TimerEngine(settings.TimerPreferences, settings.TimerSession);
        _engine.Completed += OnCompleted;
        _ticker = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _ticker.Tick += OnTick;
    }

    public event Action<TimerSnapshot>? Updated;
    public event Action<TimerCompletion>? Completed;

    public FeatureId Id => BuiltInFeatures.Timers.Id;
    public TimerSnapshot Snapshot => _engine.Snapshot;

    public void Start()
    {
        if (_started) return;
        _started = true;
        bool changed = _engine.Refresh();
        if (changed) Persist();
        _ticker.Start();
        Publish();
    }

    public void Stop()
    {
        if (!_started) return;
        _ticker.Stop();
        _started = false;
        Persist();
    }

    public void Toggle()
    {
        _engine.Toggle();
        PersistAndPublish();
    }

    public void Reset()
    {
        _engine.Reset();
        PersistAndPublish();
    }

    public void Skip()
    {
        _engine.Skip();
        PersistAndPublish();
    }

    public void SelectFocus()
    {
        _engine.SelectFocus();
        PersistAndPublish();
    }

    public void SelectCountdown()
    {
        _engine.SelectCountdown();
        PersistAndPublish();
    }

    public void SelectStopwatch()
    {
        _engine.SelectStopwatch();
        PersistAndPublish();
    }

    public void AdjustCountdown(int minutes)
    {
        int next = Math.Clamp(_engine.Preferences.CountdownMinutes + minutes, 1, 720);
        _engine.SelectCountdown(TimeSpan.FromMinutes(next));
        PersistAndPublish();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_engine.Refresh()) Persist();
        Publish();
    }

    private void OnCompleted(TimerCompletion completion)
    {
        Persist();
        try { SystemSounds.Exclamation.Play(); }
        catch { }
        Completed?.Invoke(completion);
    }

    private void PersistAndPublish()
    {
        Persist();
        Publish();
    }

    private void Persist()
    {
        _settingsStore.Save(_settings);
    }

    private void Publish()
    {
        Updated?.Invoke(_engine.Snapshot);
    }

    public void Dispose()
    {
        Stop();
        _ticker.Tick -= OnTick;
        _engine.Completed -= OnCompleted;
    }

    ValueTask IFeatureModule.StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Start();
        return ValueTask.CompletedTask;
    }

    ValueTask IFeatureModule.StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
