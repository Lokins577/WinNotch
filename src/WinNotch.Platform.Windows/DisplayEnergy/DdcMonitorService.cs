using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using WinNotch.DisplayEnergy;

namespace WinNotch.Platform.Windows.DisplayEnergy;

public sealed class DdcMonitorService
{
    private const uint BrightnessCapability = 0x00000002;

    public IReadOnlyList<ExternalMonitorInfo> Enumerate(IReadOnlyCollection<string> validatedIds)
    {
        var result = new List<ExternalMonitorInfo>();
        VisitPhysicalMonitors((logicalDevice, monitor, index) =>
        {
            string id = Id(logicalDevice, monitor.Description, index);
            uint minimum = 0, current = 0, maximum = 0;
            bool capabilities = GetMonitorCapabilities(monitor.Handle, out uint flags, out _);
            bool brightnessSupported = capabilities && (flags & BrightnessCapability) != 0
                                       && GetMonitorBrightness(monitor.Handle, out minimum, out current, out maximum);
            if (!brightnessSupported) minimum = current = maximum = 0;
            result.Add(new ExternalMonitorInfo(
                id,
                string.IsNullOrWhiteSpace(monitor.Description) ? logicalDevice : monitor.Description,
                (int)minimum,
                (int)current,
                (int)maximum,
                brightnessSupported,
                validatedIds.Contains(id, StringComparer.Ordinal)));
        });
        return result;
    }

    public bool TrySetBrightness(
        string monitorId,
        int percent,
        DisplayEnergyPreferences preferences,
        out string message)
    {
        if (!preferences.ValidatedDdcMonitorIds.Contains(monitorId, StringComparer.Ordinal))
        {
            message = "This DDC/CI monitor has not been explicitly validated by the user.";
            return false;
        }
        bool found = false;
        bool success = false;
        string detail = "The monitor is no longer available.";
        VisitPhysicalMonitors((logicalDevice, monitor, index) =>
        {
            if (Id(logicalDevice, monitor.Description, index) != monitorId) return;
            found = true;
            if (!GetMonitorCapabilities(monitor.Handle, out uint flags, out _)
                || (flags & BrightnessCapability) == 0
                || !GetMonitorBrightness(monitor.Handle, out uint minimum, out _, out uint maximum))
            {
                detail = "The monitor does not report a supported DDC/CI brightness capability.";
                return;
            }
            uint requested = minimum + (uint)Math.Round(Math.Clamp(percent, 0, 100) / 100d * (maximum - minimum));
            success = SetMonitorBrightness(monitor.Handle, requested);
            detail = success ? $"Brightness set to {percent}%." : "The monitor rejected the DDC/CI brightness request.";
        });
        message = found ? detail : "The monitor is no longer available.";
        return success;
    }

    private static void VisitPhysicalMonitors(Action<string, PhysicalMonitor, int> visitor)
    {
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (logical, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = string.Empty };
            string device = GetMonitorInfo(logical, ref info) ? info.Device : logical.ToString();
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(logical, out uint count) || count == 0) return true;
            var monitors = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(logical, count, monitors)) return true;
            try
            {
                for (int index = 0; index < monitors.Length; index++) visitor(device, monitors[index], index);
            }
            finally
            {
                foreach (var monitor in monitors)
                    if (monitor.Handle != IntPtr.Zero) DestroyPhysicalMonitor(monitor.Handle);
            }
            return true;
        }, IntPtr.Zero);
    }

    private static string Id(string device, string description, int index)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{device}\0{description}\0{index}"));
        return Convert.ToHexString(hash)[..20];
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    private delegate bool MonitorCallback(IntPtr monitor, IntPtr context, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr context, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitor(IntPtr monitor);
    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorCapabilities(IntPtr monitor, out uint capabilities, out uint temperatures);
    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);
    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);
}
