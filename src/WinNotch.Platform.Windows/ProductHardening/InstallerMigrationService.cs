using System.Diagnostics;
using Microsoft.Win32;
using WinNotch.Platform.Windows.AppManagement;

namespace WinNotch.Platform.Windows.ProductHardening;

public sealed record InstallerMigrationStatus(
    bool IsPackaged,
    bool LegacyInstallerDetected,
    string Detail,
    string? LegacyUninstallCommand);

public sealed class InstallerMigrationService
{
    private const string LegacyKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8F3A2D1-7E4C-4F5A-9D6B-1C2E3F4A5B6C}_is1";

    public InstallerMigrationStatus GetStatus()
    {
        string? command = ReadUninstall(Registry.CurrentUser) ?? ReadUninstall(Registry.LocalMachine);
        bool packaged = PackageIdentityService.HasIdentity;
        return new InstallerMigrationStatus(
            packaged,
            !string.IsNullOrWhiteSpace(command),
            packaged
                ? command == null
                    ? "Running as signed MSIX; no legacy Inno registration was detected."
                    : "Running as MSIX while a legacy Inno installation remains. Settings data is shared; remove the legacy app to avoid duplicate startup entries."
                : "Running as unpackaged/Inno build. Install the signed MSIX to enable identity-gated permissions.",
            command);
    }

    public void RemoveLegacyStartupEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
        key?.DeleteValue("WinNotch", throwOnMissingValue: false);
    }

    public Process StartLegacyUninstaller(InstallerMigrationStatus status)
    {
        if (!status.IsPackaged || string.IsNullOrWhiteSpace(status.LegacyUninstallCommand))
            throw new InvalidOperationException("No packaged-to-legacy migration action is available.");
        return InstalledApplicationService.StartRegisteredCommand(status.LegacyUninstallCommand);
    }

    private static string? ReadUninstall(RegistryKey hive)
    {
        try
        {
            using var key = hive.OpenSubKey(LegacyKey);
            return key?.GetValue("UninstallString") as string;
        }
        catch { return null; }
    }
}
