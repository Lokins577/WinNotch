using WinNotch.CommandBar;
using WinNotch.Features;
using WinNotch.Helpers;

namespace WinNotch.Services;

public sealed class CommandBarFeatureService : IFeatureModule, IDisposable
{
    private readonly CommandBarHotkey _hotkey = new();

    public CommandBarFeatureService(CommandBarEngine engine)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public FeatureId Id => BuiltInFeatures.CommandBar.Id;
    public CommandBarEngine Engine { get; }
    public bool HotkeyAvailable => _hotkey.IsRegistered;
    public event Action? OpenRequested;

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _hotkey.Pressed += OnHotkeyPressed;
        _hotkey.Register();
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        _hotkey.Pressed -= OnHotkeyPressed;
        _hotkey.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnHotkeyPressed() => OpenRequested?.Invoke();

    public void Dispose() => StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
