using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using WinNotch.SystemMonitoring;
using Forms = System.Windows.Forms;

namespace WinNotch.Platform.Windows.SystemMonitoring;

public sealed class WindowsSystemMetricsCollector : ISystemMetricsCollector
{
    private CpuTimes? _previousCpu;
    private ulong? _previousReceived;
    private ulong? _previousSent;
    private DateTimeOffset? _previousAt;
    private Dictionary<int, ProcessCounters> _previousProcesses = [];
    private DateTimeOffset _performanceMetricsAt = DateTimeOffset.MinValue;
    private DateTimeOffset _hardwareMetricsAt = DateTimeOffset.MinValue;
    private SystemMetricReading _gpu = Unavailable(SystemMetricKind.GpuUsage, "%", "GPU performance counters have not been sampled yet.");
    private SystemMetricReading _diskThroughput = Unavailable(SystemMetricKind.DiskThroughput, "B/s", "Disk performance counters have not been sampled yet.");
    private SystemMetricReading _temperature = Unsupported(SystemMetricKind.Temperature, "°C", "No supported firmware temperature provider was found.");
    private SystemMetricReading _batteryHealth = Unsupported(SystemMetricKind.BatteryHealth, "%", "Battery capacity data is not exposed by this device.");
    private SystemMetricReading _powerDraw = Unsupported(SystemMetricKind.PowerDraw, "W", "No battery power-rate provider is available.");
    private IReadOnlyList<DeviceSummary> _devices = [];
    private bool _disposed;

    public ValueTask<SystemMonitorSnapshot> CollectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ValueTask<SystemMonitorSnapshot>(Task.Run(
            () => Collect(cancellationToken), cancellationToken));
    }

    private SystemMonitorSnapshot Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        double elapsedSeconds = Math.Max(0.001, (now - (_previousAt ?? now)).TotalSeconds);
        var metrics = new List<SystemMetricReading>
        {
            ReadCpu(),
            ReadMemory(),
        };
        ReadNetwork(elapsedSeconds, metrics);
        metrics.Add(ReadDiskUsage());

        if (now - _performanceMetricsAt >= TimeSpan.FromSeconds(4))
        {
            _gpu = ReadGpu();
            _diskThroughput = ReadDiskThroughput();
            _performanceMetricsAt = now;
        }
        metrics.Add(_gpu);
        metrics.Add(_diskThroughput);

        metrics.Add(ReadBatteryLevel());
        if (now - _hardwareMetricsAt >= TimeSpan.FromSeconds(30))
        {
            _batteryHealth = ReadBatteryHealth();
            _powerDraw = ReadPowerDraw();
            _temperature = ReadTemperature();
            _devices = ReadDevices();
            _hardwareMetricsAt = now;
        }
        metrics.Add(_batteryHealth);
        metrics.Add(_powerDraw);
        metrics.Add(_temperature);

        var processes = ReadProcesses(elapsedSeconds, cancellationToken);
        _previousAt = now;
        return new SystemMonitorSnapshot(now, metrics, processes, _devices);
    }

    private SystemMetricReading ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return Unavailable(SystemMetricKind.CpuUsage, "%", "GetSystemTimes failed.");
        var current = new CpuTimes(ToUInt64(idle), ToUInt64(kernel), ToUInt64(user));
        var previous = _previousCpu;
        _previousCpu = current;
        if (previous == null)
            return Unavailable(SystemMetricKind.CpuUsage, "%", "Waiting for the second CPU sample.");

        ulong idleDelta = Delta(current.Idle, previous.Value.Idle);
        ulong totalDelta = Delta(current.Kernel, previous.Value.Kernel)
                           + Delta(current.User, previous.Value.User);
        if (totalDelta == 0)
            return Unavailable(SystemMetricKind.CpuUsage, "%", "CPU sample interval was too short.");
        double value = Math.Clamp((double)(totalDelta - Math.Min(idleDelta, totalDelta)) / totalDelta * 100, 0, 100);
        return Available(SystemMetricKind.CpuUsage, value, "%", "System-wide processor time from GetSystemTimes.");
    }

    private static SystemMetricReading ReadMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0)
            return Unavailable(SystemMetricKind.MemoryUsage, "%", "GlobalMemoryStatusEx failed.");
        ulong used = status.TotalPhysical - status.AvailablePhysical;
        double value = (double)used / status.TotalPhysical * 100;
        string detail = $"{ToGiB(used):0.0} / {ToGiB(status.TotalPhysical):0.0} GiB physical memory";
        return Available(SystemMetricKind.MemoryUsage, value, "%", detail);
    }

    private void ReadNetwork(double elapsedSeconds, ICollection<SystemMetricReading> metrics)
    {
        try
        {
            ulong received = 0;
            ulong sent = 0;
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var statistics = adapter.GetIPv4Statistics();
                received += (ulong)Math.Max(0, statistics.BytesReceived);
                sent += (ulong)Math.Max(0, statistics.BytesSent);
            }
            if (_previousReceived.HasValue && _previousSent.HasValue)
            {
                metrics.Add(Available(SystemMetricKind.NetworkDownload,
                    Delta(received, _previousReceived.Value) / elapsedSeconds, "B/s",
                    "Aggregate bytes received by active non-loopback adapters."));
                metrics.Add(Available(SystemMetricKind.NetworkUpload,
                    Delta(sent, _previousSent.Value) / elapsedSeconds, "B/s",
                    "Aggregate bytes sent by active non-loopback adapters."));
            }
            else
            {
                metrics.Add(Unavailable(SystemMetricKind.NetworkDownload, "B/s", "Waiting for the second network sample."));
                metrics.Add(Unavailable(SystemMetricKind.NetworkUpload, "B/s", "Waiting for the second network sample."));
            }
            _previousReceived = received;
            _previousSent = sent;
        }
        catch (Exception exception)
        {
            metrics.Add(Unavailable(SystemMetricKind.NetworkDownload, "B/s", exception.Message));
            metrics.Add(Unavailable(SystemMetricKind.NetworkUpload, "B/s", exception.Message));
        }
    }

    private static SystemMetricReading ReadDiskUsage()
    {
        try
        {
            long total = 0;
            long free = 0;
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                total += drive.TotalSize;
                free += drive.AvailableFreeSpace;
            }
            if (total <= 0) return Unsupported(SystemMetricKind.DiskUsage, "%", "No ready fixed drives were found.");
            double usedPercent = (double)(total - free) / total * 100;
            return Available(SystemMetricKind.DiskUsage, usedPercent, "%",
                $"{ToGiB(total - free):0} / {ToGiB(total):0} GiB across fixed drives");
        }
        catch (Exception exception)
        {
            return Unavailable(SystemMetricKind.DiskUsage, "%", exception.Message);
        }
    }

    private static SystemMetricReading ReadGpu()
    {
        try
        {
            double total = 0;
            int count = 0;
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    string name = Convert.ToString(item["Name"]) ?? string.Empty;
                    if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;
                    total += Convert.ToDouble(item["UtilizationPercentage"] ?? 0);
                    count++;
                }
            }
            return count == 0
                ? Unsupported(SystemMetricKind.GpuUsage, "%", "The Windows GPU Engine counterset returned no 3D engines.")
                : Estimated(SystemMetricKind.GpuUsage, Math.Clamp(total, 0, 100), "%",
                    "Sum of Windows GPU Engine 3D counters, capped at 100%; multi-GPU attribution is approximate.");
        }
        catch (Exception exception)
        {
            return Unsupported(SystemMetricKind.GpuUsage, "%", $"GPU Engine counters unavailable: {exception.Message}");
        }
    }

    private static SystemMetricReading ReadDiskThroughput()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT DiskBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    double value = Convert.ToDouble(item["DiskBytesPersec"] ?? 0);
                    return Available(SystemMetricKind.DiskThroughput, Math.Max(0, value), "B/s",
                        "Aggregate physical-disk transfer rate from the Windows formatted counter provider.");
                }
            }
            return Unsupported(SystemMetricKind.DiskThroughput, "B/s", "The physical-disk counterset returned no total instance.");
        }
        catch (Exception exception)
        {
            return Unavailable(SystemMetricKind.DiskThroughput, "B/s", exception.Message);
        }
    }

    private static SystemMetricReading ReadBatteryLevel()
    {
        try
        {
            var power = Forms.SystemInformation.PowerStatus;
            if (power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery))
                return Unsupported(SystemMetricKind.BatteryLevel, "%", "No system battery is present.");
            double value = Math.Clamp(power.BatteryLifePercent * 100, 0, 100);
            return Available(SystemMetricKind.BatteryLevel, value, "%",
                power.PowerLineStatus == Forms.PowerLineStatus.Online ? "Connected to external power." : "Running on battery.");
        }
        catch (Exception exception)
        {
            return Unavailable(SystemMetricKind.BatteryLevel, "%", exception.Message);
        }
    }

    private static SystemMetricReading ReadBatteryHealth()
    {
        try
        {
            double design = QueryFirstDouble("root\\wmi", "SELECT DesignedCapacity FROM BatteryStaticData", "DesignedCapacity");
            double full = QueryFirstDouble("root\\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");
            if (design <= 0 || full <= 0)
                return Unsupported(SystemMetricKind.BatteryHealth, "%", "Firmware did not report design and full-charge capacity.");
            return Estimated(SystemMetricKind.BatteryHealth, Math.Clamp(full / design * 100, 0, 200), "%",
                $"Full charge {full:0} mWh / design {design:0} mWh.");
        }
        catch (Exception exception)
        {
            return Unsupported(SystemMetricKind.BatteryHealth, "%", exception.Message);
        }
    }

    private static SystemMetricReading ReadPowerDraw()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\wmi", "SELECT Charging, ChargeRate, DischargeRate FROM BatteryStatus");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    bool charging = Convert.ToBoolean(item["Charging"] ?? false);
                    double milliwatts = Convert.ToDouble(item[charging ? "ChargeRate" : "DischargeRate"] ?? 0);
                    if (milliwatts <= 0 || milliwatts > 1_000_000) continue;
                    return Estimated(SystemMetricKind.PowerDraw, milliwatts / 1000, "W",
                        charging ? "Firmware-reported battery charge rate." : "Firmware-reported battery discharge rate.");
                }
            }
            return Unsupported(SystemMetricKind.PowerDraw, "W", "Battery firmware did not report a charge or discharge rate.");
        }
        catch (Exception exception)
        {
            return Unsupported(SystemMetricKind.PowerDraw, "W", exception.Message);
        }
    }

    private static SystemMetricReading ReadTemperature()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            var values = new List<double>();
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    double tenthsKelvin = Convert.ToDouble(item["CurrentTemperature"] ?? 0);
                    double celsius = tenthsKelvin / 10 - 273.15;
                    if (celsius is > -50 and < 250) values.Add(celsius);
                }
            }
            return values.Count == 0
                ? Unsupported(SystemMetricKind.Temperature, "°C", "ACPI exposed no thermal zones; no CPU-temperature claim is made.")
                : Estimated(SystemMetricKind.Temperature, values.Max(), "°C",
                    "Highest ACPI thermal-zone reading; this is not guaranteed to be CPU package temperature.");
        }
        catch (Exception exception)
        {
            return Unsupported(SystemMetricKind.Temperature, "°C", exception.Message);
        }
    }

    private static IReadOnlyList<DeviceSummary> ReadDevices()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Name, PNPClass FROM Win32_PnPEntity WHERE Status='OK' AND (PNPClass='Bluetooth' OR PNPClass='USB' OR PNPClass='AudioEndpoint' OR PNPClass='Monitor')");
            var result = new List<DeviceSummary>();
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    string name = Convert.ToString(item["Name"])?.Trim() ?? string.Empty;
                    string category = Convert.ToString(item["PNPClass"])?.Trim() ?? "Device";
                    if (name.Length > 0) result.Add(new DeviceSummary(name, category));
                }
            }
            return result
                .DistinctBy(device => (device.Name, device.Category))
                .OrderBy(device => device.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(20)
                .ToArray();
        }
        catch { return []; }
    }

    private IReadOnlyList<ProcessResourceSample> ReadProcesses(
        double elapsedSeconds,
        CancellationToken cancellationToken)
    {
        var current = new Dictionary<int, ProcessCounters>();
        var samples = new List<ProcessResourceSample>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    int id = process.Id;
                    string name = process.ProcessName;
                    long cpuTicks = process.TotalProcessorTime.Ticks;
                    long workingSet = process.WorkingSet64;
                    ulong ioBytes = 0;
                    if (GetProcessIoCounters(process.Handle, out var io))
                        ioBytes = io.ReadTransferCount + io.WriteTransferCount;
                    var counters = new ProcessCounters(cpuTicks, workingSet, ioBytes);
                    current[id] = counters;
                    double cpu = 0;
                    double ioRate = 0;
                    if (_previousProcesses.TryGetValue(id, out var previous))
                    {
                        cpu = Math.Clamp(
                            (double)Math.Max(0, cpuTicks - previous.CpuTicks)
                            / TimeSpan.TicksPerSecond / elapsedSeconds / Environment.ProcessorCount * 100,
                            0, 100);
                        ioRate = Delta(ioBytes, previous.IoBytes) / elapsedSeconds;
                    }
                    samples.Add(new ProcessResourceSample(id, name, cpu, workingSet, ioRate));
                }
                catch { }
            }
        }
        _previousProcesses = current;
        return samples
            .OrderByDescending(sample => sample.CpuPercent)
            .Take(6)
            .Concat(samples.OrderByDescending(sample => sample.WorkingSetBytes).Take(4))
            .Concat(samples.OrderByDescending(sample => sample.IoBytesPerSecond).Take(4))
            .DistinctBy(sample => sample.ProcessId)
            .Take(12)
            .ToArray();
    }

    private static double QueryFirstDouble(string scope, string query, string property)
    {
        using var searcher = new ManagementObjectSearcher(scope, query);
        using var results = searcher.Get();
        foreach (ManagementObject item in results)
        {
            using (item) return Convert.ToDouble(item[property] ?? 0);
        }
        return 0;
    }

    public void Dispose()
    {
        _disposed = true;
        _previousProcesses.Clear();
    }

    private static double ToGiB(double bytes) => bytes / 1024d / 1024 / 1024;
    private static ulong Delta(ulong current, ulong previous) => current >= previous ? current - previous : 0;
    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;
    private static SystemMetricReading Available(SystemMetricKind kind, double value, string unit, string detail)
        => new(kind, value, unit, MetricAvailability.Available, detail);
    private static SystemMetricReading Estimated(SystemMetricKind kind, double value, string unit, string detail)
        => new(kind, value, unit, MetricAvailability.Estimated, detail);
    private static SystemMetricReading Unavailable(SystemMetricKind kind, string unit, string detail)
        => new(kind, null, unit, MetricAvailability.Unavailable, detail);
    private static SystemMetricReading Unsupported(SystemMetricKind kind, string unit, string detail)
        => new(kind, null, unit, MetricAvailability.Unsupported, detail);

    private readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);
    private readonly record struct ProcessCounters(long CpuTicks, long WorkingSetBytes, ulong IoBytes);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
}
