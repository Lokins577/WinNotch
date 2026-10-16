using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Settings;
using WinNotch.SystemMonitoring;
using WinNotch.Views;
using WinNotch.Platform.Windows.SystemMonitoring;
using Xunit;

namespace WinNotch.Tests;

public sealed class SystemMonitorTests
{
    [Fact]
    public void History_TrimsOldestSamplesWhenCapacityChanges()
    {
        var history = new SystemMonitorHistory(3);
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        for (int index = 0; index < 5; index++)
            history.Add(Snapshot(start.AddSeconds(index), SystemMetricKind.CpuUsage, index));

        Assert.Equal([2d, 3d, 4d], history.Snapshots.Select(item => item.Find(SystemMetricKind.CpuUsage)!.Value));

        history.Resize(2);
        Assert.Equal([3d, 4d], history.Snapshots.Select(item => item.Find(SystemMetricKind.CpuUsage)!.Value));
    }

    [Fact]
    public void AlertEngine_RaisesOnceAfterSustainedThresholdAndRearmsAfterRecovery()
    {
        var engine = new SustainedMetricAlertEngine();
        var preferences = new SystemMonitorPreferences
        {
            SustainedSeconds = 10,
            CpuAlertPercent = 80,
            MemoryAlertEnabled = false
        };
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        Assert.Empty(engine.Evaluate(Snapshot(start, SystemMetricKind.CpuUsage, 90), preferences));
        Assert.Empty(engine.Evaluate(Snapshot(start.AddSeconds(9), SystemMetricKind.CpuUsage, 91), preferences));
        Assert.Single(engine.Evaluate(Snapshot(start.AddSeconds(10), SystemMetricKind.CpuUsage, 92), preferences));
        Assert.Empty(engine.Evaluate(Snapshot(start.AddSeconds(20), SystemMetricKind.CpuUsage, 99), preferences));
        Assert.Empty(engine.Evaluate(Snapshot(start.AddSeconds(21), SystemMetricKind.CpuUsage, 20), preferences));
        Assert.Empty(engine.Evaluate(Snapshot(start.AddSeconds(22), SystemMetricKind.CpuUsage, 90), preferences));
        Assert.Single(engine.Evaluate(Snapshot(start.AddSeconds(32), SystemMetricKind.CpuUsage, 90), preferences));
    }

    [Fact]
    public void AlertEngine_IgnoresUnavailableMetrics()
    {
        var snapshot = new SystemMonitorSnapshot(
            DateTimeOffset.UtcNow,
            [new SystemMetricReading(SystemMetricKind.CpuUsage, null, "%", MetricAvailability.Unavailable, "test")],
            [], []);
        var preferences = new SystemMonitorPreferences { SustainedSeconds = 2 };
        var engine = new SustainedMetricAlertEngine();

        Assert.Empty(engine.Evaluate(snapshot, preferences));
        Assert.Empty(engine.Evaluate(snapshot with { CapturedAt = snapshot.CapturedAt.AddSeconds(10) }, preferences));
    }

    [Fact]
    public async Task WindowsCollector_ReportsCompleteCapabilitySetWithoutMutatingSystem()
    {
        using var collector = new WindowsSystemMetricsCollector();

        var first = await collector.CollectAsync(CancellationToken.None);
        await Task.Delay(100);
        var second = await collector.CollectAsync(CancellationToken.None);

        Assert.Equal(Enum.GetValues<SystemMetricKind>().Length, second.Metrics.Count);
        Assert.Equal(second.Metrics.Count, second.Metrics.Select(metric => metric.Kind).Distinct().Count());
        Assert.True(second.Find(SystemMetricKind.MemoryUsage)?.HasValue);
        Assert.True(second.Find(SystemMetricKind.CpuUsage)?.HasValue);
        Assert.NotEqual(DateTimeOffset.MinValue, first.CapturedAt);
    }

    [Fact]
    public void DashboardAndSettings_BuildOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                var store = new MemoryStore();
                using var service = new SystemMonitorFeatureService(new FakeCollector(), settings.SystemMonitorPreferences);
                var dashboard = new SystemMonitorWindow(service, settings, store);
                var editor = new SystemMonitorSettingsWindow(settings, store, service);
                Assert.NotNull(dashboard.Content);
                Assert.NotNull(editor.Content);
                dashboard.Close();
                editor.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "System Monitor UI test timed out.");
        Assert.Null(failure);
    }

    private static SystemMonitorSnapshot Snapshot(
        DateTimeOffset at,
        SystemMetricKind kind,
        double value)
        => new(at,
            [new SystemMetricReading(kind, value, "%", MetricAvailability.Available, "test")],
            [], []);

    private sealed class FakeCollector : ISystemMetricsCollector
    {
        public ValueTask<SystemMonitorSnapshot> CollectAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult(SystemMonitorSnapshot.Empty);
        public void Dispose() { }
    }

    private sealed class MemoryStore : IAppSettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
        public void ApplyStartOnBoot(bool enabled) { }
    }
}
