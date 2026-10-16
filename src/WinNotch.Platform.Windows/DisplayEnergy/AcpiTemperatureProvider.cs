using System.Management;
using WinNotch.DisplayEnergy;

namespace WinNotch.Platform.Windows.DisplayEnergy;

public sealed class AcpiTemperatureProvider
{
    public HardwareMetricCapability Read()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            using var results = searcher.Get();
            var values = new List<double>();
            foreach (ManagementObject value in results)
            {
                using (value)
                {
                    double celsius = Convert.ToDouble(value["CurrentTemperature"] ?? 0) / 10 - 273.15;
                    if (celsius is > -50 and < 250) values.Add(celsius);
                }
            }
            return values.Count == 0
                ? new HardwareMetricCapability(false, "ACPI", "No thermal zones are exposed; no CPU temperature claim is made.")
                : new HardwareMetricCapability(true, "ACPI", "Highest firmware thermal-zone value; not guaranteed to be CPU package temperature.", values.Max(), "°C");
        }
        catch (Exception exception)
        {
            return new HardwareMetricCapability(false, "ACPI", exception.Message);
        }
    }
}
