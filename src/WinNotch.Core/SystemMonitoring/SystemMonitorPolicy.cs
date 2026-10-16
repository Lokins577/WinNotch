namespace WinNotch.SystemMonitoring;

public sealed class SystemMonitorHistory
{
    private readonly List<SystemMonitorSnapshot> _snapshots = [];
    private int _capacity;

    public SystemMonitorHistory(int capacity)
    {
        _capacity = Math.Clamp(capacity, 1, 1800);
    }

    public IReadOnlyList<SystemMonitorSnapshot> Snapshots => _snapshots;

    public void Resize(int capacity)
    {
        _capacity = Math.Clamp(capacity, 1, 1800);
        Trim();
    }

    public void Add(SystemMonitorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshots.Add(snapshot);
        Trim();
    }

    private void Trim()
    {
        int excess = _snapshots.Count - _capacity;
        if (excess > 0) _snapshots.RemoveRange(0, excess);
    }
}

public sealed class SustainedMetricAlertEngine
{
    private readonly Dictionary<SystemMetricKind, AlertState> _states = [];

    public IReadOnlyList<SystemMetricAlert> Evaluate(
        SystemMonitorSnapshot snapshot,
        SystemMonitorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Normalize();

        var thresholds = new List<(SystemMetricKind Kind, bool Enabled, double Value)>
        {
            (SystemMetricKind.CpuUsage, preferences.CpuAlertEnabled, preferences.CpuAlertPercent),
            (SystemMetricKind.MemoryUsage, preferences.MemoryAlertEnabled, preferences.MemoryAlertPercent),
            (SystemMetricKind.GpuUsage, preferences.GpuAlertEnabled, preferences.GpuAlertPercent)
        };
        var raised = new List<SystemMetricAlert>();
        foreach (var threshold in thresholds)
        {
            var reading = snapshot.Find(threshold.Kind);
            if (!threshold.Enabled || reading?.HasValue != true || reading.Value <= threshold.Value)
            {
                _states.Remove(threshold.Kind);
                continue;
            }

            if (!_states.TryGetValue(threshold.Kind, out var state))
            {
                _states[threshold.Kind] = new AlertState(snapshot.CapturedAt, false);
                continue;
            }
            if (state.Raised
                || snapshot.CapturedAt - state.ExceededSince < TimeSpan.FromSeconds(preferences.SustainedSeconds))
                continue;

            var alert = new SystemMetricAlert(
                threshold.Kind,
                reading.Value!.Value,
                threshold.Value,
                state.ExceededSince,
                snapshot.CapturedAt);
            _states[threshold.Kind] = state with { Raised = true };
            raised.Add(alert);
        }
        return raised;
    }

    public void Reset() => _states.Clear();

    private sealed record AlertState(DateTimeOffset ExceededSince, bool Raised);
}
