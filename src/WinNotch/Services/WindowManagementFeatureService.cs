using WinNotch.Features;
using WinNotch.Helpers;
using WinNotch.WindowManagement;

namespace WinNotch.Services;

public sealed class WindowManagementFeatureService : IFeatureModule, IDisposable
{
    private readonly WindowManagementHotkey _hotkey = new();
    private bool _disposed;

    public WindowManagementFeatureService()
    {
        Backend = new WindowsWindowManagementBackend();
    }

    public FeatureId Id => BuiltInFeatures.WindowManagement.Id;
    public IWindowManagementBackend Backend { get; }
    public bool HotkeyAvailable => _hotkey.IsRegistered;
    public event Action? OpenRequested;

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

    public void Dispose()
    {
        if (_disposed) return;
        StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        Backend.Dispose();
        _disposed = true;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
