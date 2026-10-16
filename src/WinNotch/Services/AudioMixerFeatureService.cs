using WinNotch.AudioControl;
using WinNotch.Features;
using WinNotch.Helpers;
using WinNotch.Platform.Windows.AudioControl;

namespace WinNotch.Services;

public sealed class AudioMixerFeatureService : IFeatureModule, IDisposable
{
    private readonly AudioMixerHotkey _hotkey = new();
    private readonly ProcessLoopbackAnalyzer _loopback = new();
    private bool _disposed;

    public AudioMixerFeatureService(IAudioControlBackend backend)
    {
        Backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _loopback.SpectrumUpdated += (processId, bands) => ProcessSpectrumUpdated?.Invoke(processId, bands);
    }

    public FeatureId Id => BuiltInFeatures.AudioMixer.Id;
    public IAudioControlBackend Backend { get; }
    public int? AnalyzedProcessId => _loopback.ActiveProcessId;
    public event Action? OpenRequested;
    public event Action<int, IReadOnlyList<float>>? ProcessSpectrumUpdated;

    public AudioControlSnapshot Refresh() => Backend.Refresh();
    public Task StartProcessAnalysisAsync(int processId, CancellationToken cancellationToken = default)
        => _loopback.StartAsync(processId, cancellationToken);
    public Task StopProcessAnalysisAsync() => _loopback.StopAsync();

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        _hotkey.Pressed += OnPressed;
        _hotkey.Register();
        return ValueTask.CompletedTask;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _hotkey.Pressed -= OnPressed;
        _hotkey.Dispose();
        await _loopback.StopAsync().ConfigureAwait(false);
    }

    private void OnPressed() => OpenRequested?.Invoke();

    public void Dispose()
    {
        if (_disposed) return;
        StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        _loopback.Dispose();
        Backend.Dispose();
        _disposed = true;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
