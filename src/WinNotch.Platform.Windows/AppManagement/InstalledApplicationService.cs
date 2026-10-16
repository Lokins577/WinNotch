using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using Microsoft.Win32;
using WinNotch.AppManagement;

namespace WinNotch.Platform.Windows.AppManagement;

public sealed class InstalledApplicationService
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public IReadOnlyList<InstalledApplication> GetInstalledApplications()
    {
        var result = new Dictionary<string, InstalledApplication>(StringComparer.OrdinalIgnoreCase);
        ReadHive(RegistryHive.LocalMachine, RegistryView.Registry64, true, result);
        ReadHive(RegistryHive.LocalMachine, RegistryView.Registry32, true, result);
        ReadHive(RegistryHive.CurrentUser, RegistryView.Default, false, result);
        return result.Values.OrderBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public Process StartUninstaller(InstalledApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (string.IsNullOrWhiteSpace(application.UninstallCommand))
            throw new InvalidOperationException("This application exposes no uninstall command.");
        return StartRegisteredCommand(application.UninstallCommand);
    }

    public static Process StartRegisteredCommand(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        string[] arguments = SplitCommandLine(command);
        if (arguments.Length == 0 || string.IsNullOrWhiteSpace(arguments[0]))
            throw new InvalidOperationException("The uninstall command is invalid.");
        var start = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(arguments[0])) { UseShellExecute = true };
        foreach (string argument in arguments.Skip(1)) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("The registered command did not start.");
    }

    public Process StartInstaller(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Installer not found.", fullPath);
        string extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (extension is not (".exe" or ".msi" or ".msix" or ".msixbundle" or ".appinstaller"))
            throw new InvalidOperationException("Only recognized Windows installer formats can be handed off.");
        return Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true })
               ?? throw new InvalidOperationException("The installer did not start.");
    }

    private static void ReadHive(
        RegistryHive hive,
        RegistryView view,
        bool machineWide,
        IDictionary<string, InstalledApplication> result)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(UninstallPath);
            if (root == null) return;
            foreach (string subKeyName in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(subKeyName);
                string? name = key?.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(name)) continue;
                string id = $"{hive}:{view}:{subKeyName}";
                result[id] = new InstalledApplication(
                    id,
                    name.Trim(),
                    key?.GetValue("DisplayVersion") as string,
                    key?.GetValue("Publisher") as string,
                    key?.GetValue("InstallLocation") as string,
                    key?.GetValue("UninstallString") as string,
                    machineWide);
            }
        }
        catch { }
    }

    private static string[] SplitCommandLine(string command)
    {
        IntPtr pointer = CommandLineToArgvW(command, out int count);
        if (pointer == IntPtr.Zero) return [];
        try
        {
            var result = new string[count];
            for (int index = 0; index < count; index++)
                result[index] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, index * IntPtr.Size)) ?? string.Empty;
            return result;
        }
        finally { LocalFree(pointer); }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string command, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
