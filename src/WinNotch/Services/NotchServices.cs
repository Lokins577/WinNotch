using System;
using System.Threading.Tasks;
using System.IO;
using WinNotch.Models;
using WinNotch.Features;
using WinNotch.Helpers;
using WinNotch.Settings;
using WinNotch.Platform.Windows.Clipboard;
using WinNotch.Platform.Windows.CommandBar;
using WinNotch.CommandBar;

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
    public MediaService Media { get; }
    public LyricsService Lyrics { get; }
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
    public AgentActivityService? Agents { get; private set; }
    public ClipboardHistoryService? ClipboardHistory { get; private set; }
    public CommandBarFeatureService? CommandBar { get; private set; }
    public FeatureRuntime FeatureRuntime { get; }

    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;

    private Task? _initTask;

    public NotchServices(AppSettings settings, IAppSettingsStore settingsStore)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        Media = new MediaService(settings.PreferredMediaSourceId, sourceId =>
        {
            settings.PreferredMediaSourceId = sourceId;
            settingsStore.Save(settings);
        });
        Lyrics = new LyricsService(
            Media,
            new LrclibLyricsProvider($"WinNotch/{AppInfo.Version}"),
            () => settings.OnlineLyricsEnabled);
        FeatureRuntime = new FeatureRuntime(BuiltInFeatures.Catalog);
        FeatureRuntime.Register(BuiltInFeatures.Island.Id,
            () => new NoOpFeatureModule(BuiltInFeatures.Island.Id));
        FeatureRuntime.Register(BuiltInFeatures.Timers.Id, CreateTimerModule);
        FeatureRuntime.Register(BuiltInFeatures.Downloads.Id, CreateDownloadsModule);
        FeatureRuntime.Register(BuiltInFeatures.Notifications.Id, CreateNotificationsModule);
        FeatureRuntime.Register(BuiltInFeatures.AgentActivity.Id, CreateAgentsModule);
        FeatureRuntime.Register(BuiltInFeatures.Clipboard.Id, CreateClipboardModule);
        FeatureRuntime.Register(BuiltInFeatures.CommandBar.Id, CreateCommandBarModule);
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
        await FeatureRuntime.SetEnabledAsync(
            BuiltInFeatures.AgentActivity.Id,
            _settings.IsFeatureAvailable(BuiltInFeatures.AgentActivity));
        await FeatureRuntime.SetEnabledAsync(
            BuiltInFeatures.Clipboard.Id,
            _settings.IsFeatureAvailable(BuiltInFeatures.Clipboard));
        await FeatureRuntime.SetEnabledAsync(
            BuiltInFeatures.CommandBar.Id,
            _settings.IsFeatureAvailable(BuiltInFeatures.CommandBar));
    }

    public void Dispose()
    {
        AudioCapture.Dispose();
        Lyrics.Dispose();
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

    private IFeatureModule CreateAgentsModule()
    {
        Agents = new AgentActivityService();
        return Agents;
    }

    private IFeatureModule CreateClipboardModule()
    {
        ClipboardHistory = new ClipboardHistoryService(
            _settings.ClipboardPreferences,
            new JsonClipboardHistoryStore());
        return ClipboardHistory;
    }

    private IFeatureModule CreateCommandBarModule()
    {
        var engine = new CommandBarEngine(new ICommandProvider[]
        {
            new CalculatorCommandProvider(),
            new UnitCommandProvider(),
            new EmojiCommandProvider(),
            new ApplicationCommandProvider(),
            new WindowCommandProvider(),
            new ClipboardCommandProvider(() => ClipboardHistory),
            new FileCommandProvider(GetCommandBarSearchFolders),
            new SnippetCommandProvider(() => _settings.CommandSnippets),
            new ScriptCommandProvider(() => _settings.CommandScripts)
        });
        CommandBar = new CommandBarFeatureService(engine);
        return CommandBar;
    }

    private IReadOnlyList<string> GetCommandBarSearchFolders()
    {
        if (_settings.CommandBarSearchFolders.Count > 0)
            return _settings.CommandBarSearchFolders;

        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        }.Where(Directory.Exists).ToArray();
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
