using System.Text.Json.Serialization;

namespace WinNotch.InputEnhancements;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SideButtonAction
{
    None,
    BrowserBack,
    BrowserForward,
    MiddleClick,
    CommandBar,
    QuickPanel
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SuperKeyAction
{
    None,
    CommandBar,
    QuickPanel,
    TaskView
}

public sealed class TextExpansionSnippet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Trigger { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
}

public sealed class InputEnhancementPreferences
{
    public bool ReverseVerticalScroll { get; set; }
    public bool ReverseHorizontalScroll { get; set; }
    public double ScrollMultiplier { get; set; } = 1;
    public bool FocusFollowsMouse { get; set; }
    public int FocusDelayMilliseconds { get; set; } = 250;
    public bool OverrideMouseAcceleration { get; set; }
    public int MouseSpeed { get; set; } = 10;
    public int AccelerationThreshold1 { get; set; } = 6;
    public int AccelerationThreshold2 { get; set; } = 10;
    public int AccelerationMode { get; set; } = 1;
    public SideButtonAction XButton1Action { get; set; }
    public SideButtonAction XButton2Action { get; set; }
    public int SideButtonDebounceMilliseconds { get; set; } = 120;
    public SuperKeyAction RightSuperKeyAction { get; set; }
    public bool TextExpansionEnabled { get; set; }
    public List<TextExpansionSnippet> TextExpansions { get; set; } = [];
    public List<string> ExcludedApplications { get; set; } = [];

    public void Normalize()
    {
        ScrollMultiplier = Math.Clamp(ScrollMultiplier, 0.1, 5);
        FocusDelayMilliseconds = Math.Clamp(FocusDelayMilliseconds, 50, 2000);
        MouseSpeed = Math.Clamp(MouseSpeed, 1, 20);
        AccelerationThreshold1 = Math.Clamp(AccelerationThreshold1, 0, 20);
        AccelerationThreshold2 = Math.Clamp(AccelerationThreshold2, 0, 20);
        AccelerationMode = Math.Clamp(AccelerationMode, 0, 2);
        SideButtonDebounceMilliseconds = Math.Clamp(SideButtonDebounceMilliseconds, 20, 1000);
        TextExpansions ??= [];
        TextExpansions = TextExpansions
            .Where(value => !string.IsNullOrWhiteSpace(value.Trigger) && value.Replacement != null)
            .Select(value =>
            {
                value.Trigger = value.Trigger.Trim();
                if (value.Trigger.Length > 32) value.Trigger = value.Trigger[..32];
                if (value.Replacement.Length > 4096) value.Replacement = value.Replacement[..4096];
                if (value.Id == Guid.Empty) value.Id = Guid.NewGuid();
                return value;
            })
            .DistinctBy(value => value.Trigger, StringComparer.Ordinal)
            .Take(100)
            .ToList();
        ExcludedApplications ??= [];
        ExcludedApplications = ExcludedApplications
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? value.Trim()[..^4]
                : value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
    }

    public bool IsExcluded(string? processName)
        => !string.IsNullOrWhiteSpace(processName)
           && ExcludedApplications.Contains(processName, StringComparer.OrdinalIgnoreCase);
}

public sealed record TextExpansionMatch(string Trigger, string Replacement, char Delimiter);
