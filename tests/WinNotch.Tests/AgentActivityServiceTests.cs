using System.IO;
using WinNotch.Agents;
using WinNotch.Services;
using Xunit;

namespace WinNotch.Tests;

public sealed class AgentActivityServiceTests
{
    [Fact]
    public async Task CodexMetadataAndState_AreReadWithoutPromptContent()
    {
        using var directory = new TempAgentDirectory();
        var now = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
        string log = System.IO.Path.Combine(directory.Path, "rollout-test.jsonl");
        await File.WriteAllLinesAsync(log,
        [
            "{\"type\":\"session_meta\",\"payload\":{\"id\":\"session-1\",\"cwd\":\"D:/work/WinNotch\"}}",
            "{\"type\":\"turn_context\",\"payload\":{\"model\":\"gpt-test\"}}",
            "{\"type\":\"user_message\",\"payload\":{\"text\":\"the words task_complete are private content\"}}",
            "{\"type\":\"turn_started\",\"payload\":{}}"
        ]);
        File.SetLastWriteTimeUtc(log, now.UtcDateTime);
        var source = new AgentLogSource(AgentProvider.Codex, directory.Path, "*.jsonl");
        await using var service = new AgentActivityService([source], new ManualTimeProvider(now));

        await service.StartAsync(CancellationToken.None);

        var activity = Assert.Single(service.Snapshot.Activities);
        Assert.Equal("session-1", activity.SessionId);
        Assert.Equal("WinNotch", activity.Project);
        Assert.Equal("gpt-test", activity.Model);
        Assert.Equal(AgentRunState.Working, activity.State);
        Assert.Contains(AgentProvider.Codex, service.Snapshot.AvailableProviders);
    }

    [Fact]
    public async Task TerminalSignal_MarksSessionCompleted()
    {
        using var directory = new TempAgentDirectory();
        var now = new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
        string log = System.IO.Path.Combine(directory.Path, "session.jsonl");
        await File.WriteAllLinesAsync(log,
        [
            "{\"type\":\"session_meta\",\"payload\":{\"id\":\"done\",\"cwd\":\"C:/repo\"}}",
            "{\"type\":\"turn_complete\",\"payload\":{}}"
        ]);
        File.SetLastWriteTimeUtc(log, now.UtcDateTime);
        var source = new AgentLogSource(AgentProvider.Claude, directory.Path, "*.jsonl");
        await using var service = new AgentActivityService([source], new ManualTimeProvider(now));

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(AgentRunState.Completed, Assert.Single(service.Snapshot.Activities).State);
    }

    [Fact]
    public async Task MissingProviderDirectory_IsNotReportedAvailable()
    {
        string missing = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "winnotch-agent-missing", Guid.NewGuid().ToString("N"));
        var source = new AgentLogSource(AgentProvider.OpenCode, missing, "*.log");
        await using var service = new AgentActivityService([source]);

        await service.StartAsync(CancellationToken.None);

        Assert.Empty(service.Snapshot.Activities);
        Assert.Empty(service.Snapshot.AvailableProviders);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public ManualTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class TempAgentDirectory : IDisposable
    {
        public TempAgentDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "winnotch-agent-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
