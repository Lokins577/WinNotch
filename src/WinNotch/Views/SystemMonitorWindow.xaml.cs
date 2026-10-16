using System.Windows;
using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Settings;
using WinNotch.SystemMonitoring;

namespace WinNotch.Views;

public partial class SystemMonitorWindow : Window
{
    private readonly SystemMonitorFeatureService _service;
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _store;

    public SystemMonitorWindow(
        SystemMonitorFeatureService service,
        AppSettings settings,
        IAppSettingsStore store)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        InitializeComponent();
        _service.SnapshotUpdated += OnSnapshotUpdated;
        _service.AlertRaised += OnAlertRaised;
        Render(_service.Latest);
    }

    private void OnSnapshotUpdated(SystemMonitorSnapshot snapshot)
        => Dispatcher.BeginInvoke(() => Render(snapshot));

    private void OnAlertRaised(SystemMetricAlert alert)
        => Dispatcher.BeginInvoke(() =>
        {
            AlertBanner.Visibility = Visibility.Visible;
            AlertText.Text = $"Sustained {Label(alert.Kind)}: {alert.Value:0.#}% (threshold {alert.Threshold:0.#}%)";
        });

    private void Render(SystemMonitorSnapshot snapshot)
    {
        CpuText.Text = Format(snapshot.Find(SystemMetricKind.CpuUsage));
        GpuText.Text = Format(snapshot.Find(SystemMetricKind.GpuUsage));
        MemoryText.Text = Format(snapshot.Find(SystemMetricKind.MemoryUsage));
        NetworkText.Text = $"{FormatRate(snapshot.Find(SystemMetricKind.NetworkDownload))} / {FormatRate(snapshot.Find(SystemMetricKind.NetworkUpload))}";
        DiskText.Text = $"{FormatRate(snapshot.Find(SystemMetricKind.DiskThroughput))} / {Format(snapshot.Find(SystemMetricKind.DiskUsage))}";
        PowerText.Text = Format(snapshot.Find(SystemMetricKind.PowerDraw));
        BatteryText.Text = Format(snapshot.Find(SystemMetricKind.BatteryLevel));
        BatteryHealthText.Text = Format(snapshot.Find(SystemMetricKind.BatteryHealth));
        TemperatureText.Text = Format(snapshot.Find(SystemMetricKind.Temperature));
        DeviceCountText.Text = snapshot.Devices.Count.ToString();
        DeviceList.ItemsSource = snapshot.Devices.Select(device => $"{device.Category}: {device.Name}").ToArray();
        ProcessList.ItemsSource = snapshot.Processes.Select(process => new ProcessDisplay(
            $"{process.Name} ({process.ProcessId})",
            $"{process.CpuPercent:0.0}%",
            FormatBytes(process.WorkingSetBytes),
            $"{FormatBytes(process.IoBytesPerSecond)}/s")).ToArray();

        var history = _service.GetHistory();
        CpuChart.SetValues(history.Select(item => item.Find(SystemMetricKind.CpuUsage)?.Value));
        MemoryChart.SetValues(history.Select(item => item.Find(SystemMetricKind.MemoryUsage)?.Value));
        StatusText.Text = snapshot.CapturedAt == DateTimeOffset.MinValue
            ? "Waiting for the first sample…"
            : $"Updated {snapshot.CapturedAt.ToLocalTime():T}. Network-per-process attribution is unavailable without ETW or a driver.";
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        var window = new SystemMonitorSettingsWindow(_settings, _store, _service)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    protected override void OnClosed(EventArgs e)
    {
        _service.SnapshotUpdated -= OnSnapshotUpdated;
        _service.AlertRaised -= OnAlertRaised;
        base.OnClosed(e);
    }

    private static string Format(SystemMetricReading? reading)
    {
        if (reading?.HasValue != true) return reading?.Availability.ToString() ?? "Unavailable";
        return reading.Unit == "W"
            ? $"{reading.Value:0.0} W"
            : reading.Unit == "°C"
                ? $"{reading.Value:0.0} °C"
                : $"{reading.Value:0.0}{reading.Unit}";
    }

    private static string FormatRate(SystemMetricReading? reading)
        => reading?.HasValue == true ? $"{FormatBytes(reading.Value!.Value)}/s" : reading?.Availability.ToString() ?? "Unavailable";

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        int unit = 0;
        double value = Math.Max(0, bytes);
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private static string Label(SystemMetricKind kind) => kind switch
    {
        SystemMetricKind.CpuUsage => "CPU usage",
        SystemMetricKind.MemoryUsage => "memory usage",
        SystemMetricKind.GpuUsage => "GPU usage",
        _ => kind.ToString()
    };

    private sealed record ProcessDisplay(string Name, string Cpu, string Memory, string Io);
}
