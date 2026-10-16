using System.Text.Json.Serialization;

namespace WinNotch.ProductHardening;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppLanguage
{
    Automatic,
    English,
    SimplifiedChinese
}

public sealed class ProductHardeningPreferences
{
    public AppLanguage Language { get; set; }
    public bool LocalCrashReportsEnabled { get; set; } = true;
    public bool PerformanceMonitoringEnabled { get; set; } = true;
}

public enum PermissionStatus
{
    Available,
    Allowed,
    Denied,
    NotRequested,
    Unsupported,
    RequiresPackageIdentity
}

public sealed record PermissionCapability(
    string Id,
    string DisplayName,
    PermissionStatus Status,
    string Detail,
    string? SettingsUri = null);

public sealed record PerformanceSample(
    DateTimeOffset CapturedAt,
    double UiLatencyMilliseconds,
    double ProcessCpuPercent,
    long WorkingSetBytes);

public sealed record PerformanceViolation(string Budget, double Actual, double Limit, string Unit);

public static class PerformanceBudgets
{
    public const double UiLatencyMilliseconds = 250;
    public const double BackgroundCpuPercent = 20;
    public const long WorkingSetBytes = 450L * 1024 * 1024;

    public static IReadOnlyList<PerformanceViolation> Evaluate(PerformanceSample sample)
    {
        var result = new List<PerformanceViolation>();
        if (sample.UiLatencyMilliseconds > UiLatencyMilliseconds)
            result.Add(new("UI dispatcher latency", sample.UiLatencyMilliseconds, UiLatencyMilliseconds, "ms"));
        if (sample.ProcessCpuPercent > BackgroundCpuPercent)
            result.Add(new("Process CPU", sample.ProcessCpuPercent, BackgroundCpuPercent, "%"));
        if (sample.WorkingSetBytes > WorkingSetBytes)
            result.Add(new("Working set", sample.WorkingSetBytes, WorkingSetBytes, "bytes"));
        return result;
    }
}
