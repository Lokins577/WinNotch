using WinNotch.Features;
using WinNotch.Helpers;
using WinNotch.Platform.Windows.Capture;
using WinNotch.Platform.Windows.MediaTools;

namespace WinNotch.Services;

public sealed class MediaToolsFeatureService : IFeatureModule, IDisposable
{
    private readonly MediaToolsHotkey _hotkey = new();

    public MediaToolsFeatureService()
    {
        var capture = new WindowsCaptureService();
        Recorder = new ScreenRecordingService(capture);
        Exporter = new MediaExportService(capture);
    }

    public FeatureId Id => BuiltInFeatures.MediaTools.Id;
    public ScreenRecordingService Recorder { get; }
    public MediaExportService Exporter { get; }
    public event Action? OpenRequested;

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _hotkey.Pressed += OnPressed;
        _hotkey.Register();
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _hotkey.Pressed -= OnPressed;
        _hotkey.Dispose();
        if (Recorder.IsRecording) await Recorder.StopAsync().ConfigureAwait(false);
    }

    private void OnPressed() => OpenRequested?.Invoke();

    public void Dispose()
    {
        StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        Recorder.Dispose();
    }

    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
