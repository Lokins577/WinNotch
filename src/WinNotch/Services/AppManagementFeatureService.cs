using WinNotch.AppManagement;
using WinNotch.Features;
using WinNotch.Helpers;
using WinNotch.Platform.Windows.AppManagement;

namespace WinNotch.Services;

public sealed class AppManagementFeatureService : IFeatureModule, IDisposable
{
    private readonly AppManagementHotkey _hotkey = new();

    public AppManagementFeatureService(AppManagementPreferences preferences)
    {
        Winget = new WingetService();
        InstalledApps = new InstalledApplicationService();
        Cleanup = new CleanupService(preferences);
        Ports = new PortInspectionService();
        Processes = new ProcessActionService();
    }

    public FeatureId Id => BuiltInFeatures.AppManagement.Id;
    public WingetService Winget { get; }
    public InstalledApplicationService InstalledApps { get; }
    public CleanupService Cleanup { get; }
    public PortInspectionService Ports { get; }
    public ProcessActionService Processes { get; }
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
