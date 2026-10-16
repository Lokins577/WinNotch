using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using WinNotch.Clipboard;
using WinNotch.CommandBar;
using WinNotch.Services;

namespace WinNotch.Platform.Windows.CommandBar;

public sealed class ApplicationCommandProvider : ICommandProvider
{
    private readonly Lazy<IReadOnlyList<ApplicationEntry>> _applications = new(LoadApplications);
    public string Id => "applications";

    public ValueTask<IReadOnlyList<CommandResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var results = _applications.Value
            .Select(app => (app, score: CommandScoring.Score(app.Name, query, 100)))
            .Where(value => value.score > double.NegativeInfinity)
            .OrderByDescending(value => value.score)
            .Take(string.IsNullOrWhiteSpace(query) ? 8 : 20)
            .Select(value => new CommandResult(
                $"app:{value.app.Path}",
                value.app.Name,
                "Application",
                CommandResultKind.Application,
                value.score,
                _ =>
                {
                    Process.Start(new ProcessStartInfo(value.app.Path) { UseShellExecute = true });
                    return ValueTask.CompletedTask;
                }))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<CommandResult>>(results);
    }

    private static IReadOnlyList<ApplicationEntry> LoadApplications()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };
        var entries = new Dictionary<string, ApplicationEntry>(StringComparer.CurrentCultureIgnoreCase);
        foreach (string root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    entries.TryAdd(name, new ApplicationEntry(name, file));
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return entries.Values.OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private sealed record ApplicationEntry(string Name, string Path);
}

public sealed class WindowCommandProvider : ICommandProvider
{
    public string Id => "windows";

    public ValueTask<IReadOnlyList<CommandResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var windows = EnumerateWindows();
        var results = windows
            .Select(window => (window, score: CommandScoring.Score(window.Title, query, 90)))
            .Where(value => value.score > double.NegativeInfinity)
            .OrderByDescending(value => value.score)
            .Take(string.IsNullOrWhiteSpace(query) ? 8 : 20)
            .Select(value => new CommandResult(
                $"window:{value.window.Handle}",
                value.window.Title,
                value.window.ProcessName,
                CommandResultKind.Window,
                value.score,
                _ =>
                {
                    if (IsIconic(value.window.Handle)) ShowWindow(value.window.Handle, 9);
                    SetForegroundWindow(value.window.Handle);
                    return ValueTask.CompletedTask;
                }))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<CommandResult>>(results);
    }

    private static IReadOnlyList<WindowEntry> EnumerateWindows()
    {
        var result = new List<WindowEntry>();
        uint ownProcess = (uint)Environment.ProcessId;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) <= 0) return true;
            GetWindowThreadProcessId(handle, out uint processId);
            if (processId == ownProcess) return true;
            var title = new StringBuilder(GetWindowTextLength(handle) + 1);
            GetWindowText(handle, title, title.Capacity);
            if (string.IsNullOrWhiteSpace(title.ToString())) return true;
            string processName;
            try { processName = Process.GetProcessById((int)processId).ProcessName; }
            catch { processName = "Window"; }
            result.Add(new WindowEntry(handle, title.ToString(), processName));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private sealed record WindowEntry(IntPtr Handle, string Title, string ProcessName);

    private delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr hwnd, int command);
}

public sealed class ClipboardCommandProvider : ICommandProvider
{
    private readonly Func<ClipboardHistoryService?> _clipboard;
    public ClipboardCommandProvider(Func<ClipboardHistoryService?> clipboard) => _clipboard = clipboard;
    public string Id => "clipboard";

    public ValueTask<IReadOnlyList<CommandResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var service = _clipboard();
        if (service == null) return ValueTask.FromResult<IReadOnlyList<CommandResult>>([]);
        var results = service.Search(query, string.IsNullOrWhiteSpace(query) ? 5 : 15)
            .Select((item, index) => new CommandResult(
                $"clipboard:{item.Id}",
                Preview(item),
                item.Kind.ToString(),
                CommandResultKind.Clipboard,
                300 - index,
                _ =>
                {
                    service.CopyToClipboard(item);
                    return ValueTask.CompletedTask;
                }))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<CommandResult>>(results);
    }

    private static string Preview(ClipboardHistoryItem item) => item.Kind switch
    {
        ClipboardItemKind.Text => item.Text ?? string.Empty,
        ClipboardItemKind.Files => string.Join(", ", item.Files.Select(Path.GetFileName)),
        ClipboardItemKind.Image => "Clipboard image",
        _ => "Clipboard item"
    };
}

public sealed class CalculatorCommandProvider : ICommandProvider
{
    public string Id => "calculator";
    public ValueTask<IReadOnlyList<CommandResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (!ExpressionEvaluator.TryEvaluate(query, out double value))
            return ValueTask.FromResult<IReadOnlyList<CommandResult>>([]);
        string text = value.ToString("G12", CultureInfo.InvariantCulture);
        return ValueTask.FromResult<IReadOnlyList<CommandResult>>
        ([
            new CommandResult("calculator", text, query, CommandResultKind.Calculator, 1200,
                _ => CopyText(text))
        ]);
    }

    private static ValueTask CopyText(string text)
    {
        System.Windows.Clipboard.SetText(text);
        return ValueTask.CompletedTask;
    }
}

public sealed class UnitCommandProvider : ICommandProvider
{
    public string Id => "units";
    public ValueTask<IReadOnlyList<CommandResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (!UnitConverter.TryConvert(query, out var conversion) || conversion == null)
            return ValueTask.FromResult<IReadOnlyList<CommandResult>>([]);
        return ValueTask.FromResult<IReadOnlyList<CommandResult>>
        ([
            new CommandResult("units", conversion.Display, "Unit conversion",
                CommandResultKind.UnitConversion, 1250, _ => CopyText(conversion.Output.ToString("G10", CultureInfo.InvariantCulture)))
        ]);
    }

    private static ValueTask CopyText(string text)
    {
        System.Windows.Clipboard.SetText(text);
        return ValueTask.CompletedTask;
    }
}

public sealed class EmojiCommandProvider : ICommandProvider
{
    public string Id => "emoji";
    public ValueTask<IReadOnlyList<CommandResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        string term;
        if (query.StartsWith(':')) term = query[1..];
        else if (query.StartsWith("emoji ", StringComparison.OrdinalIgnoreCase)) term = query[6..];
        else return ValueTask.FromResult<IReadOnlyList<CommandResult>>([]);
        if (string.IsNullOrWhiteSpace(term)) return ValueTask.FromResult<IReadOnlyList<CommandResult>>([]);

        var results = EmojiCatalog.Search(term)
            .Select((entry, index) => new CommandResult(
                $"emoji:{entry.Emoji}", entry.Emoji, entry.Label,
                CommandResultKind.Emoji, 900 - index,
                _ =>
                {
                    System.Windows.Clipboard.SetText(entry.Emoji);
                    return ValueTask.CompletedTask;
                }))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<CommandResult>>(results);
    }
}
