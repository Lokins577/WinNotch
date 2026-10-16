using System.IO;
using WinNotch.Downloads;
using WinNotch.Features;

namespace WinNotch.Services;

public sealed class DownloadMonitorService : IFeatureModule, IDisposable
{
    private static readonly HashSet<string> TemporaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".partial", ".download", ".tmp"
    };

    private readonly object _gate = new();
    private readonly Func<string?> _folderProvider;
    private readonly Dictionary<string, TrackedFile> _tracked = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _poller;
    private DownloadMonitorStatus _status = DownloadMonitorStatus.Stopped;
    private string? _folder;
    private string? _error;

    public DownloadMonitorService(Func<string?> folderProvider)
    {
        _folderProvider = folderProvider ?? throw new ArgumentNullException(nameof(folderProvider));
    }

    public FeatureId Id => BuiltInFeatures.Downloads.Id;
    public event Action<DownloadMonitorSnapshot>? Updated;

    public DownloadMonitorSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return CreateSnapshot();
        }
    }

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartWatching(_folderProvider());
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        StopWatching();
        return ValueTask.CompletedTask;
    }

    public void Reconfigure(string? folder)
    {
        StopWatching();
        StartWatching(folder);
    }

    private void StartWatching(string? folder)
    {
        DownloadMonitorSnapshot snapshot;
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                _folder = folder;
                _status = DownloadMonitorStatus.NeedsFolder;
                _error = null;
                snapshot = CreateSnapshot();
            }
            else
            {
                try
                {
                    _folder = Path.GetFullPath(folder);
                    _tracked.Clear();
                    _watcher = new FileSystemWatcher(_folder)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                        EnableRaisingEvents = false
                    };
                    _watcher.Created += OnCreatedOrChanged;
                    _watcher.Changed += OnCreatedOrChanged;
                    _watcher.Renamed += OnRenamed;
                    _watcher.Deleted += OnDeleted;
                    _watcher.Error += OnWatcherError;
                    _watcher.EnableRaisingEvents = true;
                    _poller = new System.Threading.Timer(Poll, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(750));
                    _status = DownloadMonitorStatus.Watching;
                    _error = null;
                    snapshot = CreateSnapshot();
                }
                catch (Exception exception)
                {
                    DisposeWatcher();
                    _status = DownloadMonitorStatus.Faulted;
                    _error = exception.Message;
                    snapshot = CreateSnapshot();
                }
            }
        }
        Updated?.Invoke(snapshot);
    }

    private void StopWatching()
    {
        DownloadMonitorSnapshot snapshot;
        lock (_gate)
        {
            DisposeWatcher();
            _tracked.Clear();
            _status = DownloadMonitorStatus.Stopped;
            _error = null;
            snapshot = CreateSnapshot();
        }
        Updated?.Invoke(snapshot);
    }

    private void OnCreatedOrChanged(object sender, FileSystemEventArgs args)
    {
        Track(args.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        lock (_gate)
            _tracked.Remove(args.OldFullPath);
        Track(args.FullPath);
    }

    private void OnDeleted(object sender, FileSystemEventArgs args)
    {
        DownloadMonitorSnapshot snapshot;
        lock (_gate)
        {
            _tracked.Remove(args.FullPath);
            snapshot = CreateSnapshot();
        }
        Updated?.Invoke(snapshot);
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        DownloadMonitorSnapshot snapshot;
        lock (_gate)
        {
            _status = DownloadMonitorStatus.Faulted;
            _error = args.GetException().Message;
            snapshot = CreateSnapshot();
        }
        Updated?.Invoke(snapshot);
    }

    private void Track(string path)
    {
        if (Directory.Exists(path)) return;

        DownloadMonitorSnapshot snapshot;
        lock (_gate)
        {
            if (!_tracked.TryGetValue(path, out var tracked))
            {
                tracked = new TrackedFile(path);
                _tracked[path] = tracked;
            }
            tracked.LastEventAt = DateTimeOffset.UtcNow;
            snapshot = CreateSnapshot();
        }
        Updated?.Invoke(snapshot);
    }

    private void Poll(object? state)
    {
        DownloadMonitorSnapshot snapshot;
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var pair in _tracked.ToArray())
            {
                var tracked = pair.Value;
                try
                {
                    var file = new FileInfo(tracked.Path);
                    if (!file.Exists)
                    {
                        _tracked.Remove(pair.Key);
                        continue;
                    }

                    if (file.Length == tracked.Bytes)
                        tracked.StableSamples++;
                    else
                    {
                        tracked.Bytes = file.Length;
                        tracked.StableSamples = 0;
                        tracked.State = DownloadActivityState.Downloading;
                    }

                    bool temporary = IsTemporary(file.Extension);
                    bool quiet = now - new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)
                        >= TimeSpan.FromSeconds(2);
                    if (!temporary && tracked.StableSamples >= 3 && quiet)
                    {
                        if (tracked.State != DownloadActivityState.Completed)
                            tracked.CompletedAt = now;
                        tracked.State = DownloadActivityState.Completed;
                    }

                    if (tracked.State == DownloadActivityState.Completed
                        && tracked.CompletedAt is { } completedAt
                        && now - completedAt >= TimeSpan.FromSeconds(10))
                    {
                        _tracked.Remove(pair.Key);
                    }
                }
                catch (IOException)
                {
                    // The producer may have the file exclusively open; retry next poll.
                }
                catch (UnauthorizedAccessException)
                {
                    // Folder access can change at runtime; keep monitoring other files.
                }
            }
            snapshot = CreateSnapshot();
        }
        Updated?.Invoke(snapshot);
    }

    private DownloadMonitorSnapshot CreateSnapshot()
    {
        var activities = _tracked.Values
            .OrderByDescending(file => file.State == DownloadActivityState.Downloading)
            .ThenByDescending(file => file.LastEventAt)
            .Take(5)
            .Select(file => new DownloadActivity(
                file.Path,
                GetDisplayName(file.Path),
                file.Bytes,
                file.State,
                file.LastEventAt))
            .ToArray();
        return new DownloadMonitorSnapshot(_status, _folder, activities, _error);
    }

    private static string GetDisplayName(string path)
    {
        string name = Path.GetFileName(path);
        while (IsTemporary(Path.GetExtension(name)))
            name = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name;
    }

    private static bool IsTemporary(string extension) => TemporaryExtensions.Contains(extension);

    private void DisposeWatcher()
    {
        _poller?.Dispose();
        _poller = null;
        if (_watcher == null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnCreatedOrChanged;
        _watcher.Changed -= OnCreatedOrChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Deleted -= OnDeleted;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        StopWatching();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class TrackedFile
    {
        public TrackedFile(string path)
        {
            Path = path;
            LastEventAt = DateTimeOffset.UtcNow;
        }

        public string Path { get; }
        public long Bytes { get; set; } = -1;
        public int StableSamples { get; set; }
        public DownloadActivityState State { get; set; } = DownloadActivityState.Downloading;
        public DateTimeOffset LastEventAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }
}
