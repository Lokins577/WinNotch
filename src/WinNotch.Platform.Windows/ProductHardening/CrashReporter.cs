using System.Text.Json;
using System.IO;

namespace WinNotch.Platform.Windows.ProductHardening;

public sealed class CrashReporter
{
    private readonly string _directory;
    private readonly Func<bool> _enabled;
    private readonly object _sync = new();

    public CrashReporter(Func<bool> enabled, string? directory = null)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _directory = Path.GetFullPath(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinNotch", "Logs", "Crashes"));
    }

    public string? Report(Exception exception, string source, bool fatal)
    {
        if (!_enabled()) return null;
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(_directory);
                string path = Path.Combine(_directory,
                    $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
                var report = new
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    Source = source,
                    Fatal = fatal,
                    ApplicationVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                    OperatingSystem = Environment.OSVersion.VersionString,
                    Exception = SerializeException(exception, 0)
                };
                File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                foreach (var old in new DirectoryInfo(_directory).GetFiles("crash-*.json")
                             .OrderByDescending(value => value.CreationTimeUtc).Skip(20))
                {
                    try { old.Delete(); } catch { }
                }
                return path;
            }
        }
        catch { return null; }
    }

    public string DirectoryPath => _directory;

    private static object SerializeException(Exception exception, int depth) => new
    {
        Type = exception.GetType().FullName,
        exception.Message,
        exception.StackTrace,
        HResult = $"0x{exception.HResult:X8}",
        Inner = depth < 5 && exception.InnerException != null
            ? SerializeException(exception.InnerException, depth + 1)
            : null
    };
}
