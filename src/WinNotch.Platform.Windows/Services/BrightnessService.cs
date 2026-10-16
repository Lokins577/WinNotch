using System;
using System.Management;

namespace WinNotch.Services;

/// <summary>
/// Monitors and controls display brightness via WMI.
/// Works on laptops with WmiMonitorBrightness support.
/// </summary>
public class BrightnessService : IDisposable
{
    private ManagementEventWatcher? _watcher;
    private bool _isSupported;

    /// <summary>Current brightness 0..100</summary>
    public int Brightness { get; private set; }

    /// <summary>Whether brightness control is available (usually laptop only)</summary>
    public bool IsSupported => _isSupported;

    /// <summary>Fired when brightness changes. Arg: brightness 0..100</summary>
    public event Action<int>? BrightnessChanged;

    public void Initialize()
    {
        try
        {
            int current = GetCurrentBrightness();
            if (current < 0)
            {
                // WMI query returned no rows — no brightness-capable display (desktop)
                _isSupported = false;
                return;
            }

            Brightness = current;
            _isSupported = true;

            // Watch for brightness change events
            var query = new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent");
            _watcher = new ManagementEventWatcher(new ManagementScope(@"\\.\root\WMI"), query);
            _watcher.EventArrived += OnBrightnessEvent;
            _watcher.Start();
        }
        catch
        {
            _isSupported = false;
        }
    }

    private void OnBrightnessEvent(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var brightness = Convert.ToInt32(e.NewEvent.Properties["Brightness"].Value);
            Brightness = brightness;
            BrightnessChanged?.Invoke(brightness);
        }
        catch { }
    }

    private static int GetCurrentBrightness()
    {
        using var searcher = new ManagementObjectSearcher(
            new ManagementScope(@"\\.\root\WMI"),
            new ObjectQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness"));

        foreach (var obj in searcher.Get())
        {
            return Convert.ToInt32(obj["CurrentBrightness"]);
        }
        return -1; // no brightness-capable display
    }

    public void SetBrightness(int level)
    {
        if (!_isSupported) return;
        level = Math.Clamp(level, 0, 100);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\root\WMI"),
                new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods"));

            foreach (ManagementObject obj in searcher.Get())
            {
                obj.InvokeMethod("WmiSetBrightness", new object[] { 1, (byte)level });
            }

            Brightness = level;
        }
        catch { }
    }

    public void Dispose()
    {
        _watcher?.Stop();
        _watcher?.Dispose();
    }
}
