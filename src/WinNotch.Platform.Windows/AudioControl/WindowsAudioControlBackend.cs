using System.Diagnostics;
using NAudio.CoreAudioApi;
using WinNotch.AudioControl;

namespace WinNotch.Platform.Windows.AudioControl;

public sealed class WindowsAudioControlBackend : IAudioControlBackend
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private bool _disposed;

    public AudioControlSnapshot Refresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string? defaultOutput = TryGetDefaultId(DataFlow.Render);
        string? defaultInput = TryGetDefaultId(DataFlow.Capture);
        var endpoints = new List<AudioEndpointInfo>();
        endpoints.AddRange(ReadEndpoints(DataFlow.Render, AudioEndpointKind.Output, defaultOutput));
        endpoints.AddRange(ReadEndpoints(DataFlow.Capture, AudioEndpointKind.Input, defaultInput));
        var sessions = defaultOutput == null ? [] : ReadSessions(defaultOutput);
        int build = Environment.OSVersion.Version.Build;
        var capability = build >= 20348
            ? new ProcessLoopbackCapability(true,
                "Windows process-loopback activation is available for per-process analysis.")
            : new ProcessLoopbackCapability(false,
                $"Windows build {build} is below the required build 20348.");
        return new AudioControlSnapshot(DateTimeOffset.UtcNow, endpoints, sessions, capability);
    }

    public bool SetSessionVolume(string sessionId, float volume)
        => WithSession(sessionId, session => session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0, 1));

    public bool SetSessionMute(string sessionId, bool muted)
        => WithSession(sessionId, session => session.SimpleAudioVolume.Mute = muted);

    public bool SetInputVolume(string endpointId, float volume)
        => WithInput(endpointId, endpoint => endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0, 1));

    public bool SetInputMute(string endpointId, bool muted)
        => WithInput(endpointId, endpoint => endpoint.AudioEndpointVolume.Mute = muted);

    public void OpenWindowsSoundSettings() => OpenSettings("ms-settings:sound");
    public void OpenWindowsAppRoutingSettings() => OpenSettings("ms-settings:apps-volume");

    private IReadOnlyList<AudioEndpointInfo> ReadEndpoints(
        DataFlow flow,
        AudioEndpointKind kind,
        string? defaultId)
    {
        var result = new List<AudioEndpointInfo>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                try
                {
                    result.Add(new AudioEndpointInfo(
                        device.ID,
                        device.DeviceFriendlyName,
                        kind,
                        string.Equals(device.ID, defaultId, StringComparison.Ordinal),
                        device.AudioEndpointVolume.MasterVolumeLevelScalar,
                        device.AudioEndpointVolume.Mute));
                }
                catch { }
            }
        }
        return result;
    }

    private IReadOnlyList<AudioSessionInfo> ReadSessions(string endpointId)
    {
        var result = new List<AudioSessionInfo>();
        using var device = _enumerator.GetDevice(endpointId);
        var sessions = device.AudioSessionManager.Sessions;
        for (int index = 0; index < sessions.Count; index++)
        {
            using var session = sessions[index];
            try
            {
                int processId = (int)session.GetProcessID;
                string name = session.DisplayName;
                if (string.IsNullOrWhiteSpace(name) && processId > 0)
                {
                    try { name = Process.GetProcessById(processId).ProcessName; }
                    catch { name = "Application"; }
                }
                if (string.IsNullOrWhiteSpace(name)) name = session.IsSystemSoundsSession ? "System sounds" : "Audio session";
                result.Add(new AudioSessionInfo(
                    session.GetSessionInstanceIdentifier,
                    processId,
                    name,
                    session.SimpleAudioVolume.Volume,
                    session.SimpleAudioVolume.Mute,
                    session.AudioMeterInformation.MasterPeakValue,
                    session.IsSystemSoundsSession));
            }
            catch { }
        }
        return result;
    }

    private bool WithSession(string sessionId, Action<AudioSessionControl> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string? endpointId = TryGetDefaultId(DataFlow.Render);
        if (endpointId == null) return false;
        using var device = _enumerator.GetDevice(endpointId);
        var sessions = device.AudioSessionManager.Sessions;
        for (int index = 0; index < sessions.Count; index++)
        {
            using var session = sessions[index];
            try
            {
                if (!string.Equals(session.GetSessionInstanceIdentifier, sessionId, StringComparison.Ordinal)) continue;
                action(session);
                return true;
            }
            catch { }
        }
        return false;
    }

    private bool WithInput(string endpointId, Action<MMDevice> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            using var endpoint = _enumerator.GetDevice(endpointId);
            if (endpoint.DataFlow != DataFlow.Capture || endpoint.State != DeviceState.Active) return false;
            action(endpoint);
            return true;
        }
        catch { return false; }
    }

    private string? TryGetDefaultId(DataFlow flow)
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return device.ID;
        }
        catch { return null; }
    }

    private static void OpenSettings(string uri)
        => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _enumerator.Dispose();
    }
}
