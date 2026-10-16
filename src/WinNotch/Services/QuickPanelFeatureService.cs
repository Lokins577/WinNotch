using WinNotch.Features;
using WinNotch.Helpers;

namespace WinNotch.Services;

public sealed class QuickPanelFeatureService : IFeatureModule, IDisposable
{
    private readonly QuickPanelHotkey _hotkey = new();
    public FeatureId Id => BuiltInFeatures.QuickPanel.Id;
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
