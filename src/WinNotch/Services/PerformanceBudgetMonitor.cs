using System.Diagnostics;
using System.Windows.Threading;
using WinNotch.ProductHardening;

namespace WinNotch.Services;

public sealed class PerformanceBudgetMonitor : IDisposable
{
    private readonly Func<bool> _enabled;
    private readonly DispatcherTimer _timer;
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private DateTimeOffset _lastSample;
    private DateTimeOffset _expectedTick;
    private readonly Dictionary<string, int> _streaks = [];

    public PerformanceBudgetMonitor(Func<bool> enabled)
    {
        _enabled = enabled;
        _lastCpu = _process.TotalProcessorTime;
        _lastSample = DateTimeOffset.UtcNow;
        _expectedTick = _lastSample.AddSeconds(5);
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _timer.Tick += OnTick;
    }

    public PerformanceSample Latest { get; private set; } = new(DateTimeOffset.MinValue, 0, 0, 0);
    public event Action<PerformanceViolation>? SustainedViolation;

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        _process.Refresh();
        TimeSpan cpu = _process.TotalProcessorTime;
        double elapsed = Math.Max(0.001, (now - _lastSample).TotalSeconds);
        double cpuPercent = Math.Clamp(
            (cpu - _lastCpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100,
            0, 100);
        double latency = Math.Max(0, (now - _expectedTick).TotalMilliseconds);
        Latest = new PerformanceSample(now, latency, cpuPercent, _process.WorkingSet64);
        _lastCpu = cpu;
        _lastSample = now;
        _expectedTick = now.Add(_timer.Interval);
        if (!_enabled())
        {
            _streaks.Clear();
            return;
        }
        var violations = PerformanceBudgets.Evaluate(Latest);
        var active = violations.Select(value => value.Budget).ToHashSet(StringComparer.Ordinal);
        foreach (string cleared in _streaks.Keys.Where(key => !active.Contains(key)).ToArray())
            _streaks.Remove(cleared);
        foreach (var violation in violations)
        {
            int streak = _streaks.TryGetValue(violation.Budget, out int current) ? current + 1 : 1;
            _streaks[violation.Budget] = streak;
            if (streak == 3) SustainedViolation?.Invoke(violation);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _process.Dispose();
    }
}
