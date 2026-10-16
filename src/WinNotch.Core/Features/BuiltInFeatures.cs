namespace WinNotch.Features;

public static class BuiltInFeatures
{
    public static readonly FeatureDescriptor Island = Available(
        "island.core", "Dynamic Island", FeatureGroup.Island,
        "Core island window, interaction, positioning, and multi-monitor behavior.");

    public static readonly FeatureDescriptor Media = Available(
        "island.media", "Media", FeatureGroup.Island,
        "Now-playing information, transport controls, progress, and visualizer.",
        dependencies: new[] { Island.Id });

    public static readonly FeatureDescriptor Calendar = Available(
        "island.calendar", "Calendar", FeatureGroup.Island,
        "Upcoming calendar events in the expanded island.", dependencies: new[] { Island.Id });

    public static readonly FeatureDescriptor Battery = Available(
        "island.battery", "Battery", FeatureGroup.Island,
        "Battery state and compact island indicator.", dependencies: new[] { Island.Id });

    public static readonly FeatureDescriptor Shelf = Available(
        "files.shelf", "Shelf", FeatureGroup.ClipboardAndFiles,
        "Temporary file staging through drag and drop.", dependencies: new[] { Island.Id });

    public static readonly FeatureDescriptor Camera = Available(
        "tools.camera", "Camera preview", FeatureGroup.Tools,
        "Local camera mirror in the island.", enabledByDefault: false, dependencies: new[] { Island.Id });

    public static readonly FeatureDescriptor SystemHud = Available(
        "island.system-hud", "Volume and brightness HUD", FeatureGroup.Island,
        "Compact feedback and controls for system volume and brightness.", dependencies: new[] { Island.Id });

    public static readonly FeatureDescriptor Plugins = Available(
        "platform.plugins", "Plugins", FeatureGroup.Platform,
        "Third-party plugin discovery, lifecycle, and UI hosting.", dependencies: new[] { Island.Id });

    public static readonly FeatureDescriptor Timers = new(
        "island.timers", "Timers and stopwatch", FeatureGroup.Island,
        "Timers, focus cycles, stopwatch, alarms, and live indicators.",
        EnabledByDefault: true,
        Maturity: FeatureMaturity.Available,
        Dependencies: new[] { Island.Id });

    public static readonly FeatureDescriptor Notifications = new(
        "island.notifications", "Notification activities", FeatureGroup.Island,
        "Permission-based notification previews and inbox actions.",
        EnabledByDefault: false,
        Maturity: FeatureMaturity.Experimental,
        Dependencies: new[] { Island.Id });

    public static readonly FeatureDescriptor Downloads = new(
        "island.downloads", "Download activities", FeatureGroup.Island,
        "Monitor a chosen folder and show active or completed downloads in the island.",
        EnabledByDefault: false,
        Maturity: FeatureMaturity.Available,
        Dependencies: new[] { Island.Id });

    public static readonly FeatureDescriptor AgentActivity = new(
        "island.agents", "AI agent activity", FeatureGroup.Island,
        "Follow local Codex, Claude Code, OpenCode, and GitHub Copilot sessions without uploading logs.",
        EnabledByDefault: false,
        Maturity: FeatureMaturity.Experimental,
        Dependencies: new[] { Island.Id });

    public static readonly FeatureDescriptor Clipboard = new(
        "files.clipboard", "Clipboard history", FeatureGroup.ClipboardAndFiles,
        "Searchable local clipboard history and privacy policy.",
        EnabledByDefault: false,
        Maturity: FeatureMaturity.Available,
        Dependencies: new[] { Island.Id });

    public static readonly FeatureDescriptor CommandBar = new(
        "tools.command-bar", "Command bar", FeatureGroup.Tools,
        "Unified local search, calculations, commands, and snippets.",
        EnabledByDefault: true,
        Maturity: FeatureMaturity.Available,
        Dependencies: new[] { Island.Id });

    public static readonly FeatureDescriptor QuickPanel = new(
        "tools.quick-panel", "Quick Panel", FeatureGroup.Tools,
        "A compact launcher for WinNotch surfaces and safe system actions.",
        EnabledByDefault: true,
        Maturity: FeatureMaturity.Available);

    public static readonly FeatureDescriptor Scratchpad = new(
        "tools.scratchpad", "Scratchpad", FeatureGroup.Tools,
        "Persistent local notes with tabs and lightweight Markdown preview.",
        EnabledByDefault: false,
        Maturity: FeatureMaturity.Available);

    public static readonly FeatureDescriptor RadialMenu = new(
        "tools.radial-menu", "Radial menu", FeatureGroup.Tools,
        "A configurable pointer-centered wheel of typed actions.",
        EnabledByDefault: false,
        Maturity: FeatureMaturity.Available);

    public static readonly FeatureDescriptor WindowManagement = new(
        "windows.management", "Window management", FeatureGroup.Windows,
        "Switcher, live previews, layouts, restore, and multi-monitor movement.",
        EnabledByDefault: false,
        Maturity: FeatureMaturity.Available);

    public static readonly FeatureDescriptor SystemMonitor = new(
        "monitor.system", "System monitor", FeatureGroup.Monitor,
        "Resource metrics, history, process attribution, sensors, and sustained alerts.",
        EnabledByDefault: false,
        Maturity: FeatureMaturity.Available);

    public static readonly FeatureDescriptor AudioMixer = new(
        "sound.mixer", "Per-app audio", FeatureGroup.Sound,
        "Session volume, process analysis, devices, microphone, and supported routing handoff.",
        EnabledByDefault: false,
        Maturity: FeatureMaturity.Available);

    public static readonly FeatureDescriptor Capture = new(
        "tools.capture", "Capture and OCR", FeatureGroup.Tools,
        "Screenshots, annotation, redaction, pinning, offline OCR, QR, and color capture.",
        EnabledByDefault: false,
        Maturity: FeatureMaturity.Available);

    public static readonly FeatureDescriptor MediaTools = new(
        "tools.media-tools", "Recording and media tools", FeatureGroup.Tools,
        "Screen recording, local editing, GIF/video export, conversion, compression, and watermarking.",
        EnabledByDefault: false,
        Maturity: FeatureMaturity.Available);

    public static readonly FeatureDescriptor AppManagement = Planned(
        "tools.app-management", "Application management", FeatureGroup.Tools,
        "Updates, packages, cleaner, uninstaller, ports, and process tools.");

    public static readonly FeatureDescriptor InputEnhancements = Planned(
        "input.enhancements", "Input enhancements", FeatureGroup.Input,
        "Mouse, scrolling, keyboard, snippets, debounce, and app exclusions.");

    public static readonly FeatureDescriptor DisplayAndEnergy = Planned(
        "energy.display", "Display and energy", FeatureGroup.EnergyAndDisplay,
        "Keep Awake, external displays, dimming, sleep policy, and hardware providers.");

    public static FeatureCatalog Catalog { get; } = new(new[]
    {
        Island, Media, Calendar, Battery, Shelf, Camera, SystemHud, Plugins,
        Timers, Notifications, Downloads, AgentActivity, Clipboard, CommandBar,
        QuickPanel, Scratchpad, RadialMenu, WindowManagement,
        SystemMonitor, AudioMixer, Capture, MediaTools, AppManagement, InputEnhancements,
        DisplayAndEnergy
    });

    private static FeatureDescriptor Available(
        string id,
        string name,
        FeatureGroup group,
        string description,
        bool enabledByDefault = true,
        IReadOnlyList<FeatureId>? dependencies = null)
    {
        return new FeatureDescriptor(
            id, name, group, description, enabledByDefault,
            Dependencies: dependencies);
    }

    private static FeatureDescriptor Planned(
        string id,
        string name,
        FeatureGroup group,
        string description)
    {
        return new FeatureDescriptor(
            id, name, group, description,
            EnabledByDefault: false,
            Maturity: FeatureMaturity.Planned);
    }
}
