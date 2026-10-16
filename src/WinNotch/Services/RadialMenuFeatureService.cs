using WinNotch.Features;
using WinNotch.Helpers;

namespace WinNotch.Services;

public sealed class RadialMenuFeatureService : IFeatureModule, IDisposable
{
    private readonly RadialMenuHotkey _hotkey = new();
    public FeatureId Id => BuiltInFeatures.RadialMenu.Id;
    public bool HotkeyAvailable => _hotkey.IsRegistered;
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
