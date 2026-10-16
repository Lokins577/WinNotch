using WinNotch.Features;
using WinNotch.Helpers;
using WinNotch.SystemMonitoring;

namespace WinNotch.Services;

public sealed class SystemMonitorFeatureService : IFeatureModule, IDisposable
{
    private readonly ISystemMetricsCollector _collector;
    private readonly SystemMonitorPreferences _preferences;
    private readonly SystemMonitorHistory _history;
    private readonly SustainedMetricAlertEngine _alerts = new();
    private readonly SystemMonitorHotkey _hotkey = new();
    private readonly object _sync = new();
    private readonly List<SystemMetricAlert> _alertHistory = [];
    private CancellationTokenSource? _cancellation;
    private Task? _monitorTask;
    private bool _disposed;

    public SystemMonitorFeatureService(
        ISystemMetricsCollector collector,
        SystemMonitorPreferences preferences)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _preferences.Normalize();
        _history = new SystemMonitorHistory(_preferences.HistorySamples);
    }

    public FeatureId Id => BuiltInFeatures.SystemMonitor.Id;
    public SystemMonitorSnapshot Latest { get; private set; } = SystemMonitorSnapshot.Empty;
    public bool HotkeyAvailable => _hotkey.IsRegistered;
    public event Action? OpenRequested;
    public event Action<SystemMonitorSnapshot>? SnapshotUpdated;
    public event Action<SystemMetricAlert>? AlertRaised;

    public IReadOnlyList<SystemMonitorSnapshot> GetHistory()
    {
        lock (_sync) return _history.Snapshots.ToArray();
    }

    public IReadOnlyList<SystemMetricAlert> GetAlerts()
    {
        lock (_sync) return _alertHistory.ToArray();
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_monitorTask != null) return ValueTask.CompletedTask;
        _preferences.Normalize();
        _history.Resize(_preferences.HistorySamples);
        _hotkey.Pressed += OnHotkeyPressed;
        _hotkey.Register();
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitorTask = MonitorAsync(_cancellation.Token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _hotkey.Pressed -= OnHotkeyPressed;
        _hotkey.Dispose();
        var task = _monitorTask;
        _monitorTask = null;
        if (task == null) return;
        _cancellation?.Cancel();
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _cancellation?.Dispose();
        _cancellation = null;
        _alerts.Reset();
    }

    public void ApplyPreferences()
    {
        _preferences.Normalize();
        lock (_sync) _history.Resize(_preferences.HistorySamples);
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await _collector.CollectAsync(cancellationToken).ConfigureAwait(false);
                var raised = _alerts.Evaluate(snapshot, _preferences);
                lock (_sync)
                {
                    Latest = snapshot;
                    _history.Add(snapshot);
                    foreach (var alert in raised) _alertHistory.Add(alert);
                    if (_alertHistory.Count > 50)
                        _alertHistory.RemoveRange(0, _alertHistory.Count - 50);
                }
                PublishSnapshot(snapshot);
                foreach (var alert in raised) PublishAlert(alert);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // A failed provider sample must not terminate future monitoring.
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_preferences.SampleIntervalSeconds),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private void PublishSnapshot(SystemMonitorSnapshot snapshot)
    {
        foreach (Action<SystemMonitorSnapshot> handler in SnapshotUpdated?.GetInvocationList()
                     ?? Array.Empty<Delegate>())
        {
            try { handler(snapshot); }
            catch { }
        }
    }

    private void PublishAlert(SystemMetricAlert alert)
    {
        foreach (Action<SystemMetricAlert> handler in AlertRaised?.GetInvocationList()
                     ?? Array.Empty<Delegate>())
        {
            try { handler(alert); }
            catch { }
        }
    }

    private void OnHotkeyPressed() => OpenRequested?.Invoke();

    public void Dispose()
    {
        if (_disposed) return;
        StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        _collector.Dispose();
        _disposed = true;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
