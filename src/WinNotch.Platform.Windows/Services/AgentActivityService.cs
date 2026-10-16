using System.IO;
using System.Text;
using System.Text.Json;
using WinNotch.Agents;
using WinNotch.Features;

namespace WinNotch.Services;

public sealed record AgentLogSource(
    AgentProvider Provider,
    string Root,
    string Pattern,
    SearchOption SearchOption = SearchOption.AllDirectories);

public sealed class AgentActivityService : IFeatureModule, IDisposable
{
    private const int TailByteLimit = 128 * 1024;
    private readonly IReadOnlyList<AgentLogSource> _sources;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private System.Threading.Timer? _timer;
    private AgentActivitySnapshot _snapshot;

    public AgentActivityService(
        IReadOnlyList<AgentLogSource>? sources = null,
        TimeProvider? timeProvider = null)
    {
        _sources = sources ?? CreateDefaultSources();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _snapshot = new AgentActivitySnapshot([], new HashSet<AgentProvider>(), _timeProvider.GetUtcNow());
    }

    public FeatureId Id => BuiltInFeatures.AgentActivity.Id;
    public AgentActivitySnapshot Snapshot => _snapshot;
    public event Action<AgentActivitySnapshot>? Updated;

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        await ScanAsync(cancellationToken).ConfigureAwait(false);
        _timer ??= new System.Threading.Timer(
            async _ => await ScanSafelyAsync().ConfigureAwait(false),
            null,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(3));
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        return ValueTask.CompletedTask;
    }

    public async Task ScanAsync(CancellationToken cancellationToken = default)
    {
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var available = new HashSet<AgentProvider>();
            var activities = new List<AgentActivity>();
            foreach (var source in _sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(source.Root)) continue;
                available.Add(source.Provider);

                foreach (var file in EnumerateRecentFiles(source, 5))
                {
                    var activity = TryReadActivity(source.Provider, file);
                    if (activity != null) activities.Add(activity);
                }
            }

            _snapshot = new AgentActivitySnapshot(
                activities
                    .OrderBy(activity => activity.State == AgentRunState.Working ? 0 : 1)
                    .ThenByDescending(activity => activity.UpdatedAt)
                    .Take(12)
                    .ToArray(),
                available,
                _timeProvider.GetUtcNow());
            Updated?.Invoke(_snapshot);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task ScanSafelyAsync()
    {
        try { await ScanAsync().ConfigureAwait(false); }
        catch { }
    }

    private IEnumerable<FileInfo> EnumerateRecentFiles(AgentLogSource source, int count)
    {
        try
        {
            return new DirectoryInfo(source.Root)
                .EnumerateFiles(source.Pattern, source.SearchOption)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(count)
                .ToArray();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private AgentActivity? TryReadActivity(AgentProvider provider, FileInfo file)
    {
        try
        {
            var first = ReadFirstLine(file.FullName);
            var tail = ReadTail(file.FullName, TailByteLimit);
            var metadata = new AgentMetadata();
            var signals = new List<string>();

            ReadStructuredMetadata(first, metadata, signals);
            foreach (var line in tail.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                ReadStructuredMetadata(line, metadata, signals);
            if (provider == AgentProvider.Copilot)
                ReadCopilotWorkspace(file.DirectoryName, metadata);

            string sessionId = metadata.SessionId
                ?? file.Directory?.Name
                ?? Path.GetFileNameWithoutExtension(file.Name);
            string project = ProjectName(metadata.ProjectPath) ?? provider.ToString();
            var updated = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            var state = DetermineState(signals, updated);
            return new AgentActivity(provider, sessionId, project, metadata.Model, state, updated);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    private AgentRunState DetermineState(IReadOnlyList<string> signals, DateTimeOffset updated)
    {
        string latest = signals.LastOrDefault()?.ToLowerInvariant() ?? string.Empty;
        if (ContainsAny(latest, "failed", "error", "aborted", "cancelled"))
            return AgentRunState.Failed;
        if (ContainsAny(latest, "task_complete", "turn_complete", "turn.completed",
                "session.idle", "assistant.turn_end", "completed", "result"))
            return AgentRunState.Completed;
        if (ContainsAny(latest, "working", "in_progress", "task_started", "turn_started",
                "assistant.turn_start"))
            return AgentRunState.Working;
        return _timeProvider.GetUtcNow() - updated <= TimeSpan.FromSeconds(20)
            ? AgentRunState.Working
            : AgentRunState.Idle;
    }

    private static void ReadStructuredMetadata(
        string? line,
        AgentMetadata metadata,
        List<string> signals)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        try
        {
            using var document = JsonDocument.Parse(line);
            Visit(document.RootElement, metadata, signals);
        }
        catch (JsonException)
        {
            // OpenCode and Copilot can emit non-JSON lines; freshness still works.
        }
    }

    private static void Visit(JsonElement element, AgentMetadata metadata, List<string> signals)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                string name = property.Name;
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    string? value = property.Value.GetString();
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    if (metadata.SessionId == null && name is "id" or "session_id" or "sessionId")
                        metadata.SessionId = value;
                    else if (metadata.ProjectPath == null && name is "cwd" or "projectPath" or "workspace")
                        metadata.ProjectPath = value;
                    else if (metadata.Model == null && name is "model" or "model_name")
                        metadata.Model = value;
                    if (name is "type" or "status" or "outcome" or "phase")
                        signals.Add(value);
                }
                else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    Visit(property.Value, metadata, signals);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                Visit(item, metadata, signals);
        }
    }

    private static void ReadCopilotWorkspace(string? directory, AgentMetadata metadata)
    {
        if (directory == null || metadata.ProjectPath != null) return;
        string path = Path.Combine(directory, "workspace.yaml");
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadLines(path).Take(80))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("cwd:", StringComparison.OrdinalIgnoreCase)) continue;
            metadata.ProjectPath = trimmed[4..].Trim().Trim('"', '\'');
            break;
        }
    }

    private static string ReadFirstLine(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: false);
        return reader.ReadLine() ?? string.Empty;
    }

    private static string ReadTail(string path, int byteLimit)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, stream.Length - byteLimit);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: false);
        if (start > 0) reader.ReadLine();
        return reader.ReadToEnd();
    }

    private static string? ProjectName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string normalized = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(normalized);
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<AgentLogSource> CreateDefaultSources()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME")
            ?? Path.Combine(home, ".codex");
        string claudeHome = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")
            ?? Path.Combine(home, ".claude");
        string copilotHome = Environment.GetEnvironmentVariable("COPILOT_HOME")
            ?? Path.Combine(home, ".copilot");
        string openCodeData = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(home, ".local", "share");

        return
        [
            new AgentLogSource(AgentProvider.Codex, Path.Combine(codexHome, "sessions"), "rollout-*.jsonl"),
            new AgentLogSource(AgentProvider.Claude, Path.Combine(claudeHome, "projects"), "*.jsonl"),
            new AgentLogSource(AgentProvider.OpenCode, Path.Combine(openCodeData, "opencode", "log"), "*.log"),
            new AgentLogSource(AgentProvider.Copilot, Path.Combine(copilotHome, "session-state"), "events.jsonl")
        ];
    }

    public void Dispose()
    {
        StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        _scanGate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class AgentMetadata
    {
        public string? SessionId { get; set; }
        public string? ProjectPath { get; set; }
        public string? Model { get; set; }
    }
}
