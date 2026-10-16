namespace WinNotch.Agents;

public enum AgentProvider
{
    Codex,
    Claude,
    OpenCode,
    Copilot
}

public enum AgentRunState
{
    Idle,
    Working,
    Completed,
    Failed
}

public sealed record AgentActivity(
    AgentProvider Provider,
    string SessionId,
    string Project,
    string? Model,
    AgentRunState State,
    DateTimeOffset UpdatedAt);

public sealed record AgentActivitySnapshot(
    IReadOnlyList<AgentActivity> Activities,
    IReadOnlySet<AgentProvider> AvailableProviders,
    DateTimeOffset ObservedAt);
