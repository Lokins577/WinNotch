using WinNotch.AudioControl;
using WinNotch.Models;
using WinNotch.Platform.Windows.AudioControl;
using WinNotch.Services;
using WinNotch.Settings;
using WinNotch.Views;
using Xunit;

namespace WinNotch.Tests;

public sealed class AudioControlTests
{
    [Fact]
    public void Preferences_NormalizeIdentifiersAndBoundPriority()
    {
        var preferences = new AudioControlPreferences
        {
            PreferredVisualizerOutputId = "  output-a  ",
            OutputDevicePriority = Enumerable.Range(0, 40)
                .Select(index => index < 2 ? " output-a " : $"output-{index}")
                .Append(" ")
                .ToList()
        };

        preferences.Normalize();

        Assert.Equal("output-a", preferences.PreferredVisualizerOutputId);
        Assert.Equal(32, preferences.OutputDevicePriority.Count);
        Assert.Equal(preferences.OutputDevicePriority.Count,
            preferences.OutputDevicePriority.Distinct(StringComparer.Ordinal).Count());
        Assert.All(preferences.OutputDevicePriority, id => Assert.Equal(id.Trim(), id));
    }

    [Fact]
    public void WindowsBackend_ReadOnlyRefreshReturnsCapabilityAndUniqueEndpoints()
    {
        using var backend = new WindowsAudioControlBackend();

        var snapshot = backend.Refresh();

        Assert.NotEqual(DateTimeOffset.MinValue, snapshot.CapturedAt);
        Assert.Equal(snapshot.Endpoints.Count,
            snapshot.Endpoints.Select(endpoint => endpoint.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Environment.OSVersion.Version.Build >= 20348, snapshot.ProcessLoopback.Supported);
    }

    [Fact]
    public async Task ProcessLoopback_CanAnalyzeOwnSilentProcessWhenSupported()
    {
        if (Environment.OSVersion.Version.Build < 20348) return;
        using var analyzer = new ProcessLoopbackAnalyzer(bandCount: 4, fftSize: 256);

        await analyzer.StartAsync(Environment.ProcessId);
        await Task.Delay(100);
        await analyzer.StopAsync();

        Assert.Null(analyzer.ActiveProcessId);
    }

    [Fact]
    public void MixerWindow_BuildsOnStaThreadWithoutChangingAudio()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                var store = new MemoryStore();
                using var service = new AudioMixerFeatureService(new FakeBackend());
                using var capture = new AudioCaptureService();
                var window = new AudioMixerWindow(service, settings, store, capture);
                Assert.NotNull(window.Content);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Audio mixer UI test timed out.");
        Assert.Null(failure);
    }

    private sealed class FakeBackend : IAudioControlBackend
    {
        public AudioControlSnapshot Refresh() => new(
            DateTimeOffset.UtcNow,
            [new AudioEndpointInfo("out", "Speakers", AudioEndpointKind.Output, true, 0.5f, false)],
            [new AudioSessionInfo("session", 100, "Player", 0.5f, false, 0.2f, false)],
            new ProcessLoopbackCapability(false, "test"));
        public bool SetSessionVolume(string sessionId, float volume) => true;
        public bool SetSessionMute(string sessionId, bool muted) => true;
        public bool SetInputVolume(string endpointId, float volume) => true;
        public bool SetInputMute(string endpointId, bool muted) => true;
        public void OpenWindowsSoundSettings() { }
        public void OpenWindowsAppRoutingSettings() { }
        public void Dispose() { }
    }

    private sealed class MemoryStore : IAppSettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
        public void ApplyStartOnBoot(bool enabled) { }
    }
}
