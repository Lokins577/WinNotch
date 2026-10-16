using System.Text.Json.Serialization;

namespace WinNotch.QuickTools;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RadialActionKind
{
    CommandBar = 0,
    Scratchpad = 1,
    Features = 2,
    ToggleMute = 3,
    ToggleTheme = 4,
    Lock = 5,
    Application = 6,
    File = 7,
    Url = 8,
    WindowSwitcher = 9,
    SystemMonitor = 10,
    AudioMixer = 11,
    Capture = 12,
    MediaTools = 13,
    AppManagement = 14,
    InputSettings = 15,
    DisplayEnergy = 16
}

public sealed class RadialMenuItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = string.Empty;
    public RadialActionKind Kind { get; set; }
    public string? Target { get; set; }
}

public sealed class RadialMenuConfiguration
{
    public const int MaximumItems = 12;

    public List<RadialMenuItem> Items { get; set; } = [];

    public static RadialMenuConfiguration CreateDefault() => new()
    {
        Items =
        [
            new() { Label = "Command", Kind = RadialActionKind.CommandBar },
            new() { Label = "Notes", Kind = RadialActionKind.Scratchpad },
            new() { Label = "Features", Kind = RadialActionKind.Features },
            new() { Label = "Mute", Kind = RadialActionKind.ToggleMute },
            new() { Label = "Theme", Kind = RadialActionKind.ToggleTheme },
            new() { Label = "Lock", Kind = RadialActionKind.Lock }
        ]
    };

    public void Normalize()
    {
        Items ??= [];
        Items = Items
            .Where(item => item != null)
            .Where(item => !string.IsNullOrWhiteSpace(item.Label))
            .Take(MaximumItems)
            .ToList();
        var ids = new HashSet<Guid>();
        foreach (var item in Items)
        {
            if (item.Id == Guid.Empty || !ids.Add(item.Id))
            {
                item.Id = Guid.NewGuid();
                ids.Add(item.Id);
            }
            item.Label = item.Label.Trim();
            if (item.Label.Length > 32) item.Label = item.Label[..32];
            item.Target = item.Target?.Trim();
            if (!RadialActionTarget.RequiresTarget(item.Kind)) item.Target = null;
        }
        if (Items.Count == 0) Items = CreateDefault().Items;
    }
}

public static class RadialActionTarget
{
    public static bool RequiresTarget(RadialActionKind kind) => kind is
        RadialActionKind.Application or RadialActionKind.File or RadialActionKind.Url;

    public static bool TryResolve(
        RadialActionKind kind,
        string? target,
        out string normalizedTarget,
        out string error)
    {
        normalizedTarget = string.Empty;
        error = string.Empty;

        if (!RequiresTarget(kind)) return true;
        if (string.IsNullOrWhiteSpace(target))
        {
            error = "Choose a target for this action.";
            return false;
        }

        if (kind == RadialActionKind.Url)
        {
            if (!Uri.TryCreate(target.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                error = "Enter an absolute HTTP or HTTPS URL.";
                return false;
            }
            normalizedTarget = uri.AbsoluteUri;
            return true;
        }

        string path;
        try
        {
            string candidate = target.Trim();
            if (!Path.IsPathRooted(candidate))
            {
                error = "Choose an absolute local path.";
                return false;
            }
            path = Path.GetFullPath(candidate);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "The target path is invalid.";
            return false;
        }

        bool exists = kind == RadialActionKind.Application
            ? File.Exists(path)
            : File.Exists(path) || Directory.Exists(path);
        if (!exists)
        {
            error = kind == RadialActionKind.Application
                ? "The application no longer exists."
                : "The file or folder no longer exists.";
            return false;
        }

        normalizedTarget = path;
        return true;
    }
}
