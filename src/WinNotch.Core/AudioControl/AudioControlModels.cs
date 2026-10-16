namespace WinNotch.AudioControl;

public enum AudioEndpointKind
{
    Output,
    Input
}

public sealed record AudioEndpointInfo(
    string Id,
    string Name,
    AudioEndpointKind Kind,
    bool IsDefault,
    float Volume,
    bool IsMuted);

public sealed record AudioSessionInfo(
    string Id,
    int ProcessId,
    string Name,
    float Volume,
    bool IsMuted,
    float Peak,
    bool IsSystemSounds);

public sealed record ProcessLoopbackCapability(
    bool Supported,
    string Detail,
    int MinimumWindowsBuild = 20348);

public sealed record AudioControlSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<AudioEndpointInfo> Endpoints,
    IReadOnlyList<AudioSessionInfo> Sessions,
    ProcessLoopbackCapability ProcessLoopback);

public sealed class AudioControlPreferences
{
    public string? PreferredVisualizerOutputId { get; set; }
    public List<string> OutputDevicePriority { get; set; } = [];

    public void Normalize()
    {
        PreferredVisualizerOutputId = string.IsNullOrWhiteSpace(PreferredVisualizerOutputId)
            ? null
            : PreferredVisualizerOutputId.Trim();
        OutputDevicePriority ??= [];
        OutputDevicePriority = OutputDevicePriority
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(32)
            .ToList();
    }
}

public interface IAudioControlBackend : IDisposable
{
    AudioControlSnapshot Refresh();
    bool SetSessionVolume(string sessionId, float volume);
    bool SetSessionMute(string sessionId, bool muted);
    bool SetInputVolume(string endpointId, float volume);
    bool SetInputMute(string endpointId, bool muted);
    void OpenWindowsSoundSettings();
    void OpenWindowsAppRoutingSettings();
}
