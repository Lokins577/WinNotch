namespace WinNotch.Downloads;

public enum DownloadMonitorStatus
{
    Stopped,
    NeedsFolder,
    Watching,
    Faulted
}

public enum DownloadActivityState
{
    Downloading,
    Completed
}

public sealed record DownloadActivity(
    string Path,
    string DisplayName,
    long Bytes,
    DownloadActivityState State,
    DateTimeOffset UpdatedAt);

public sealed record DownloadMonitorSnapshot(
    DownloadMonitorStatus Status,
    string? Folder,
    IReadOnlyList<DownloadActivity> Activities,
    string? Error = null);
