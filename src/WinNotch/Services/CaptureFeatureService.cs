using WinNotch.Features;
using WinNotch.Helpers;
using WinNotch.Platform.Windows.Capture;

namespace WinNotch.Services;

public sealed class CaptureFeatureService : IFeatureModule, IDisposable
{
    private readonly CaptureHotkey _hotkey = new();
    public CaptureFeatureService()
    {
        Capture = new WindowsCaptureService();
        Recognition = new OfflineImageRecognitionService();
        Scrolling = new ScrollingCaptureService(Capture);
    }

    public FeatureId Id => BuiltInFeatures.Capture.Id;
    public WindowsCaptureService Capture { get; }
    public OfflineImageRecognitionService Recognition { get; }
    public ScrollingCaptureService Scrolling { get; }
    public event Action? OpenRequested;

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _hotkey.Pressed += OnPressed;
        _hotkey.Register();
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _hotkey.Pressed -= OnPressed;
        _hotkey.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnPressed() => OpenRequested?.Invoke();
    public void Dispose() => StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
