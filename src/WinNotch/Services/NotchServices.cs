using System;
using System.Threading.Tasks;
using WinNotch.Models;
using WinNotch.Features;
using WinNotch.Settings;

namespace WinNotch.Services;

/// <summary>
/// Container for the system-level services shared by every notch window.
/// Exactly one instance exists per app run: with "island on all screens"
/// enabled, all windows bind to the same media session, audio capture,
/// volume endpoint, etc. instead of duplicating them.
/// Owned and disposed by <see cref="WinNotch.App"/>.
/// </summary>
public sealed class NotchServices : IDisposable
{
    public MediaService Media { get; } = new();
    public AudioCaptureService AudioCapture { get; } = new(bandCount: 12);
    public VolumeService Volume { get; } = new();
    public BrightnessService Brightness { get; } = new();
    public BatteryService Battery { get; } = new();
    public CalendarService Calendar { get; } = new();
    public ShelfService Shelf { get; } = new();
    public FullscreenService Fullscreen { get; } = new();
    public WebcamService Webcam { get; } = new();
    public TimerFeatureService? Timer { get; private set; }
    public DownloadMonitorService? Downloads { get; private set; }
    public NotificationListenerService? Notifications { get; private set; }
    public FeatureRuntime FeatureRuntime { get; }

    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;

    private Task? _initTask;

    public NotchServices(AppSettings settings, IAppSettingsStore settingsStore)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        FeatureRuntime = new FeatureRuntime(BuiltInFeatures.Catalog);
        FeatureRuntime.Register(BuiltInFeatures.Island.Id,
            () => new NoOpFeatureModule(BuiltInFeatures.Island.Id));
        FeatureRuntime.Register(BuiltInFeatures.Timers.Id, CreateTimerModule);
        FeatureRuntime.Register(BuiltInFeatures.Downloads.Id, CreateDownloadsModule);
        FeatureRuntime.Register(BuiltInFeatures.Notifications.Id, CreateNotificationsModule);
    }

    /// <summary>
    /// One-time global initialization; safe to await from every window
    /// (the first caller runs it, later callers await the same task).
    /// </summary>
    public Task EnsureInitializedAsync(AppSettings settings)
    {
        return _initTask ??= InitializeAsync(settings);
    }

    private async Task InitializeAsync(AppSettings settings)
    {
        await Media.InitializeAsync();
        AudioCapture.Start();
        Volume.Initialize();
        Brightness.Initialize();
        Battery.Initialize();
        Calendar.Initialize();
        Webcam.SetTargetFps(settings.WebcamFps);
        Fullscreen.Start();
        await FeatureRuntime.SetEnabledAsync(BuiltInFeatures.Island.Id, true);
        await FeatureRuntime.SetEnabledAsync(
            BuiltInFeatures.Timers.Id,
            _settings.IsFeatureAvailable(BuiltInFeatures.Timers));
        await FeatureRuntime.SetEnabledAsync(
            BuiltInFeatures.Downloads.Id,
            _settings.IsFeatureAvailable(BuiltInFeatures.Downloads));
        await FeatureRuntime.SetEnabledAsync(
            BuiltInFeatures.Notifications.Id,
            _settings.IsFeatureAvailable(BuiltInFeatures.Notifications));
    }

    public void Dispose()
    {
        AudioCapture.Dispose();
        Media.Dispose();
        Volume.Dispose();
        Brightness.Dispose();
        Battery.Dispose();
        Calendar.Dispose();
        Fullscreen.Dispose();
        Webcam.Dispose();
        FeatureRuntime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private IFeatureModule CreateTimerModule()
    {
        Timer = new TimerFeatureService(_settings, _settingsStore);
        return Timer;
    }

    private IFeatureModule CreateDownloadsModule()
    {
        Downloads = new DownloadMonitorService(() => _settings.DownloadsFolder);
        return Downloads;
    }

    private IFeatureModule CreateNotificationsModule()
    {
        Notifications = new NotificationListenerService();
        return Notifications;
    }

    private sealed class NoOpFeatureModule : IFeatureModule
    {
        public NoOpFeatureModule(FeatureId id) => Id = id;
        public FeatureId Id { get; }
        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
