using Microsoft.VisualBasic.FileIO;
using System.IO;
using WinNotch.AppManagement;

namespace WinNotch.Platform.Windows.AppManagement;

public sealed class CleanupService
{
    private readonly AppManagementPreferences _preferences;
    private readonly IReadOnlyList<ScanRoot>? _overrideRoots;

    public CleanupService(
        AppManagementPreferences preferences,
        IReadOnlyList<(string Path, string Category, int MinimumAgeDays)>? roots = null)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _overrideRoots = roots?.Select(value => new ScanRoot(
            System.IO.Path.GetFullPath(value.Path), value.Category, value.MinimumAgeDays)).ToArray();
    }

    public Task<IReadOnlyList<CleanupCandidate>> ScanAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<CleanupCandidate>>(() => Scan(cancellationToken), cancellationToken);

    public IReadOnlyList<CleanupCandidate> Scan(CancellationToken cancellationToken = default)
    {
        _preferences.Normalize();
        var result = new List<CleanupCandidate>();
        foreach (var root in GetRoots())
        {
            if (!Directory.Exists(root.Path)) continue;
            DateTime cutoff = DateTime.UtcNow.AddDays(-root.MinimumAgeDays);
            var pending = new Stack<string>();
            pending.Push(root.Path);
            while (pending.Count > 0 && result.Count < 5000)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pending.Pop();
                try
                {
                    foreach (string child in Directory.EnumerateDirectories(directory))
                    {
                        var attributes = File.GetAttributes(child);
                        if (!attributes.HasFlag(FileAttributes.ReparsePoint)) pending.Push(child);
                    }
                    foreach (string file in Directory.EnumerateFiles(directory))
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc > cutoff || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        result.Add(new CleanupCandidate(
                            info.FullName, root.Category, info.Length,
                            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
                        if (result.Count >= 5000) break;
                    }
                }
                catch { }
            }
        }
        return result.OrderByDescending(value => value.SizeBytes).ToArray();
    }

    public (int Recycled, IReadOnlyList<string> Failures) Recycle(
        IEnumerable<CleanupCandidate> candidates)
    {
        var roots = GetRoots().Select(value => value.Path).ToArray();
        int recycled = 0;
        var failures = new List<string>();
        foreach (var candidate in candidates)
        {
            try
            {
                string path = System.IO.Path.GetFullPath(candidate.Path);
                if (!roots.Any(root => IsInsideRoot(path, root)))
                    throw new InvalidOperationException("Candidate is outside configured cleanup roots.");
                if (!File.Exists(path)) continue;
                FileSystem.DeleteFile(
                    path,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin,
                    UICancelOption.ThrowException);
                recycled++;
            }
            catch (Exception exception) { failures.Add($"{candidate.Path}: {exception.Message}"); }
        }
        return (recycled, failures);
    }

    private IReadOnlyList<ScanRoot> GetRoots()
    {
        if (_overrideRoots != null) return _overrideRoots;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<ScanRoot>
        {
            new(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), "Temporary files", _preferences.TemporaryFileMinimumAgeDays),
            new(System.IO.Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"), "Edge cache", _preferences.TemporaryFileMinimumAgeDays),
            new(System.IO.Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"), "Chrome cache", _preferences.TemporaryFileMinimumAgeDays),
            new(System.IO.Path.Combine(user, "Downloads", "Telegram Desktop"), "Messaging downloads", _preferences.MessagingRetentionDays),
            new(System.IO.Path.Combine(user, "Downloads", "WhatsApp"), "Messaging downloads", _preferences.MessagingRetentionDays)
        };
        foreach (string path in _preferences.MessagingDownloadFolders)
        {
            try
            {
                if (System.IO.Path.IsPathRooted(path))
                    roots.Add(new ScanRoot(System.IO.Path.GetFullPath(path), "Messaging downloads", _preferences.MessagingRetentionDays));
            }
            catch { }
        }
        return roots
            .Where(value => !string.IsNullOrWhiteSpace(value.Path))
            .DistinctBy(value => value.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsInsideRoot(string path, string root)
    {
        string fullRoot = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar)
                          + System.IO.Path.DirectorySeparatorChar;
        return path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ScanRoot(string Path, string Category, int MinimumAgeDays);
}
