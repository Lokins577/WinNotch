namespace WinNotch.AppManagement;

public sealed record PackageUpgrade(
    string Name,
    string PackageId,
    string InstalledVersion,
    string AvailableVersion,
    string Source);

public sealed record InstalledApplication(
    string Id,
    string Name,
    string? Version,
    string? Publisher,
    string? InstallLocation,
    string? UninstallCommand,
    bool IsMachineWide);

public sealed record CleanupCandidate(
    string Path,
    string Category,
    long SizeBytes,
    DateTimeOffset LastModified,
    bool Recoverable = true);

public sealed record PortOwner(
    string Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    string State,
    int ProcessId,
    string ProcessName);

public readonly record struct ProcessActionResult(bool Success, string Message);

public sealed class AppManagementPreferences
{
    public int TemporaryFileMinimumAgeDays { get; set; } = 7;
    public int MessagingRetentionDays { get; set; } = 30;
    public List<string> MessagingDownloadFolders { get; set; } = [];

    public void Normalize()
    {
        TemporaryFileMinimumAgeDays = Math.Clamp(TemporaryFileMinimumAgeDays, 1, 365);
        MessagingRetentionDays = Math.Clamp(MessagingRetentionDays, 1, 3650);
        MessagingDownloadFolders ??= [];
        MessagingDownloadFolders = MessagingDownloadFolders
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();
    }
}

public static class ProcessTerminationPolicy
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle", "system", "registry", "smss", "csrss", "wininit", "services",
        "lsass", "winlogon", "dwm", "explorer", "fontdrvhost", "secure system"
    };

    public static bool CanTarget(int processId, string? processName, out string reason)
    {
        if (processId <= 4 || processId == Environment.ProcessId)
        {
            reason = "WinNotch and system processes cannot be targeted.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(processName) || ProtectedNames.Contains(processName))
        {
            reason = "The selected process is protected by WinNotch policy.";
            return false;
        }
        reason = string.Empty;
        return true;
    }
}
