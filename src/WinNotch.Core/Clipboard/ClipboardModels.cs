using System.Security.Cryptography;
using System.Text;

namespace WinNotch.Clipboard;

public enum ClipboardItemKind
{
    Text,
    Image,
    Files
}

public sealed class ClipboardHistoryItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ClipboardItemKind Kind { get; set; }
    public string? Text { get; set; }
    public string[] Files { get; set; } = [];
    public byte[]? ImagePng { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsPinned { get; set; }
    public string? SourceApplication { get; set; }
    public string Signature { get; set; } = string.Empty;

    public string SearchText => Kind switch
    {
        ClipboardItemKind.Text => Text ?? string.Empty,
        ClipboardItemKind.Files => string.Join(' ', Files.Select(Path.GetFileName)),
        _ => "Image"
    };
}

public sealed class ClipboardPreferences
{
    public int MaximumItems { get; set; } = 100;
    public bool CaptureImages { get; set; } = true;
    public bool CaptureFiles { get; set; } = true;
    public List<string> IgnoredApplications { get; set; } = [];

    public void Normalize()
    {
        MaximumItems = Math.Clamp(MaximumItems, 10, 500);
        IgnoredApplications = IgnoredApplications
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
    }
}

public interface IClipboardHistoryStore
{
    IReadOnlyList<ClipboardHistoryItem> Load();
    void Save(IReadOnlyList<ClipboardHistoryItem> items);
}

public sealed record ClipboardHistorySnapshot(
    IReadOnlyList<ClipboardHistoryItem> Items,
    string? Error = null);

public sealed class ClipboardHistory
{
    private readonly List<ClipboardHistoryItem> _items;
    private readonly ClipboardPreferences _preferences;

    public ClipboardHistory(ClipboardPreferences preferences, IEnumerable<ClipboardHistoryItem>? items = null)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _preferences.Normalize();
        _items = items?
            .Where(IsValid)
            .OrderByDescending(item => item.IsPinned)
            .ThenByDescending(item => item.CapturedAt)
            .ToList() ?? [];
        Trim();
    }

    public IReadOnlyList<ClipboardHistoryItem> Items => _items;

    public bool Add(ClipboardHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Normalize(item);
        if (!IsValid(item)) return false;

        item.Signature = ComputeSignature(item);
        var existing = _items.FirstOrDefault(value => value.Signature == item.Signature);
        if (existing != null)
        {
            _items.Remove(existing);
            item.Id = existing.Id;
            item.IsPinned = existing.IsPinned;
        }

        _items.Insert(0, item);
        Sort();
        Trim();
        return true;
    }

    public bool Remove(Guid id)
    {
        int index = _items.FindIndex(item => item.Id == id);
        if (index < 0) return false;
        _items.RemoveAt(index);
        return true;
    }

    public bool SetPinned(Guid id, bool pinned)
    {
        var item = _items.FirstOrDefault(value => value.Id == id);
        if (item == null) return false;
        item.IsPinned = pinned;
        Sort();
        Trim();
        return true;
    }

    public IReadOnlyList<ClipboardHistoryItem> Search(string? query, int limit = 50)
    {
        IEnumerable<ClipboardHistoryItem> result = _items;
        if (!string.IsNullOrWhiteSpace(query))
            result = result.Where(item => item.SearchText.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase));
        return result.Take(Math.Clamp(limit, 1, 500)).ToArray();
    }

    public void ClearUnpinned()
    {
        _items.RemoveAll(item => !item.IsPinned);
    }

    public static string ComputeSignature(ClipboardHistoryItem item)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData([(byte)item.Kind]);
        switch (item.Kind)
        {
            case ClipboardItemKind.Text:
                hash.AppendData(Encoding.UTF8.GetBytes(item.Text ?? string.Empty));
                break;
            case ClipboardItemKind.Files:
                foreach (var file in item.Files.Order(StringComparer.OrdinalIgnoreCase))
                    hash.AppendData(Encoding.UTF8.GetBytes(file.ToUpperInvariant()));
                break;
            case ClipboardItemKind.Image:
                hash.AppendData(item.ImagePng ?? []);
                break;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private void Sort()
    {
        _items.Sort((left, right) =>
        {
            int pinned = right.IsPinned.CompareTo(left.IsPinned);
            return pinned != 0 ? pinned : right.CapturedAt.CompareTo(left.CapturedAt);
        });
    }

    private void Trim()
    {
        int unpinnedCount = _items.Count(item => !item.IsPinned);
        while (unpinnedCount > _preferences.MaximumItems)
        {
            int index = _items.FindLastIndex(item => !item.IsPinned);
            if (index < 0) break;
            _items.RemoveAt(index);
            unpinnedCount--;
        }
    }

    private static void Normalize(ClipboardHistoryItem item)
    {
        item.Text = item.Text?.Trim();
        if (item.Text?.Length > 100_000) item.Text = item.Text[..100_000];
        item.Files = item.Files
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToArray();
        if (item.ImagePng?.Length > 5 * 1024 * 1024) item.ImagePng = null;
        if (item.CapturedAt == default) item.CapturedAt = DateTimeOffset.UtcNow;
    }

    private static bool IsValid(ClipboardHistoryItem item)
    {
        return item.Kind switch
        {
            ClipboardItemKind.Text => !string.IsNullOrWhiteSpace(item.Text),
            ClipboardItemKind.Files => item.Files.Length > 0,
            ClipboardItemKind.Image => item.ImagePng is { Length: > 0 },
            _ => false
        };
    }
}
