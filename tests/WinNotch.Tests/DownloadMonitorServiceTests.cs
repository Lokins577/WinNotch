using System.IO;
using WinNotch.Downloads;
using WinNotch.Services;
using Xunit;

namespace WinNotch.Tests;

public sealed class DownloadMonitorServiceTests
{
    [Fact]
    public async Task MissingFolder_ReportsSetupState()
    {
        await using var service = new DownloadMonitorService(() => null);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(DownloadMonitorStatus.NeedsFolder, service.Snapshot.Status);
        Assert.Empty(service.Snapshot.Activities);
    }

    [Fact]
    public async Task TemporaryDownload_RenamesAndCompletes()
    {
        using var directory = new TempDownloadDirectory();
        await using var service = new DownloadMonitorService(() => directory.Path);
        await service.StartAsync(CancellationToken.None);

        string temporary = System.IO.Path.Combine(directory.Path, "archive.zip.crdownload");
        await File.WriteAllBytesAsync(temporary, new byte[4096]);

        var downloading = await WaitForAsync(service, snapshot =>
            snapshot.Activities.Any(item =>
                item.DisplayName == "archive.zip"
                && item.State == DownloadActivityState.Downloading));
        Assert.Equal(DownloadMonitorStatus.Watching, downloading.Status);

        string completedPath = System.IO.Path.Combine(directory.Path, "archive.zip");
        File.Move(temporary, completedPath);
        File.SetLastWriteTimeUtc(completedPath, DateTime.UtcNow.AddSeconds(-5));

        var completed = await WaitForAsync(service, snapshot =>
            snapshot.Activities.Any(item =>
                item.DisplayName == "archive.zip"
                && item.State == DownloadActivityState.Completed),
            TimeSpan.FromSeconds(8));
        Assert.Equal(4096, completed.Activities.Single().Bytes);

        await service.StopAsync(CancellationToken.None);
        Assert.Equal(DownloadMonitorStatus.Stopped, service.Snapshot.Status);
    }

    private static async Task<DownloadMonitorSnapshot> WaitForAsync(
        DownloadMonitorService service,
        Func<DownloadMonitorSnapshot, bool> predicate,
        TimeSpan? timeout = null)
    {
        if (predicate(service.Snapshot)) return service.Snapshot;

        var completion = new TaskCompletionSource<DownloadMonitorSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnUpdated(DownloadMonitorSnapshot snapshot)
        {
            if (predicate(snapshot)) completion.TrySetResult(snapshot);
        }

        service.Updated += OnUpdated;
        try
        {
            return await completion.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
        }
        finally
        {
            service.Updated -= OnUpdated;
        }
    }

    private sealed class TempDownloadDirectory : IDisposable
    {
        public TempDownloadDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "winnotch-download-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
