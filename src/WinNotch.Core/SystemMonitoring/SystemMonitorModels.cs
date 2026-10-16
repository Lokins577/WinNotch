namespace WinNotch.SystemMonitoring;

public enum SystemMetricKind
{
    CpuUsage,
    GpuUsage,
    MemoryUsage,
    NetworkDownload,
    NetworkUpload,
    DiskThroughput,
    DiskUsage,
    PowerDraw,
    BatteryLevel,
    BatteryHealth,
    Temperature
}

public enum MetricAvailability
{
    Available,
    Estimated,
    Unavailable,
    Unsupported
}

public sealed record SystemMetricReading(
    SystemMetricKind Kind,
    double? Value,
    string Unit,
    MetricAvailability Availability,
    string Detail)
{
    public bool HasValue => Value.HasValue
        && Availability is MetricAvailability.Available or MetricAvailability.Estimated;
}

public sealed record ProcessResourceSample(
    int ProcessId,
    string Name,
    double CpuPercent,
    long WorkingSetBytes,
    double IoBytesPerSecond);

public sealed record DeviceSummary(string Name, string Category);

public sealed record SystemMonitorSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<SystemMetricReading> Metrics,
    IReadOnlyList<ProcessResourceSample> Processes,
    IReadOnlyList<DeviceSummary> Devices)
{
    public SystemMetricReading? Find(SystemMetricKind kind)
        => Metrics.FirstOrDefault(metric => metric.Kind == kind);

    public static SystemMonitorSnapshot Empty { get; } = new(
        DateTimeOffset.MinValue, [], [], []);
}

public sealed class SystemMonitorPreferences
{
    public int SampleIntervalSeconds { get; set; } = 2;
    public int HistorySamples { get; set; } = 180;
    public int SustainedSeconds { get; set; } = 10;
    public bool CpuAlertEnabled { get; set; } = true;
    public double CpuAlertPercent { get; set; } = 90;
    public bool MemoryAlertEnabled { get; set; } = true;
    public double MemoryAlertPercent { get; set; } = 90;
    public bool GpuAlertEnabled { get; set; }
    public double GpuAlertPercent { get; set; } = 95;

    public void Normalize()
    {
        SampleIntervalSeconds = Math.Clamp(SampleIntervalSeconds, 1, 30);
        HistorySamples = Math.Clamp(HistorySamples, 30, 1800);
        SustainedSeconds = Math.Clamp(SustainedSeconds, 2, 300);
        CpuAlertPercent = Math.Clamp(CpuAlertPercent, 1, 100);
        MemoryAlertPercent = Math.Clamp(MemoryAlertPercent, 1, 100);
        GpuAlertPercent = Math.Clamp(GpuAlertPercent, 1, 100);
    }
}

public sealed record SystemMetricAlert(
    SystemMetricKind Kind,
    double Value,
    double Threshold,
    DateTimeOffset ExceededSince,
    DateTimeOffset RaisedAt);

public interface ISystemMetricsCollector : IDisposable
{
    ValueTask<SystemMonitorSnapshot> CollectAsync(CancellationToken cancellationToken);
}
