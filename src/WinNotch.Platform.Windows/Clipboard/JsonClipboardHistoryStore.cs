using System.IO;
using System.Text.Json;
using WinNotch.Clipboard;

namespace WinNotch.Platform.Windows.Clipboard;

public sealed class JsonClipboardHistoryStore : IClipboardHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _root;
    private readonly string _imagesDirectory;
    private readonly string _metadataPath;

    public JsonClipboardHistoryStore(string? root = null)
    {
        _root = Path.GetFullPath(root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinNotch", "Clipboard"));
        _imagesDirectory = Path.Combine(_root, "images");
        _metadataPath = Path.Combine(_root, "history.json");
    }

    public IReadOnlyList<ClipboardHistoryItem> Load()
    {
        try
        {
            if (!File.Exists(_metadataPath)) return [];
            var records = JsonSerializer.Deserialize<List<Record>>(
                File.ReadAllText(_metadataPath), JsonOptions) ?? [];
            var items = new List<ClipboardHistoryItem>(records.Count);
            foreach (var record in records)
            {
                byte[]? image = null;
                if (!string.IsNullOrWhiteSpace(record.ImageFile))
                {
                    string imagePath = Path.Combine(_imagesDirectory, Path.GetFileName(record.ImageFile));
                    var info = new FileInfo(imagePath);
                    if (info.Exists && info.Length is > 0 and <= 5 * 1024 * 1024)
                        image = File.ReadAllBytes(imagePath);
                }

                items.Add(new ClipboardHistoryItem
                {
                    Id = record.Id,
                    Kind = record.Kind,
                    Text = record.Text,
                    Files = record.Files ?? [],
                    ImagePng = image,
                    CapturedAt = record.CapturedAt,
                    IsPinned = record.IsPinned,
                    SourceApplication = record.SourceApplication,
                    Signature = record.Signature ?? string.Empty
                });
            }
            return items;
        }
        catch
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<ClipboardHistoryItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_imagesDirectory);

        var referencedImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var records = new List<Record>(items.Count);
        foreach (var item in items)
        {
            string? imageFile = null;
            if (item.Kind == ClipboardItemKind.Image && item.ImagePng is { Length: > 0 } image)
            {
                imageFile = $"{item.Id:N}.png";
                referencedImages.Add(imageFile);
                string destination = Path.Combine(_imagesDirectory, imageFile);
                string temporary = destination + ".tmp";
                File.WriteAllBytes(temporary, image);
                File.Move(temporary, destination, overwrite: true);
            }

            records.Add(new Record
            {
                Id = item.Id,
                Kind = item.Kind,
                Text = item.Text,
                Files = item.Files,
                ImageFile = imageFile,
                CapturedAt = item.CapturedAt,
                IsPinned = item.IsPinned,
                SourceApplication = item.SourceApplication,
                Signature = item.Signature
            });
        }

        string metadataTemp = _metadataPath + ".tmp";
        File.WriteAllText(metadataTemp, JsonSerializer.Serialize(records, JsonOptions));
        File.Move(metadataTemp, _metadataPath, overwrite: true);

        foreach (var image in Directory.EnumerateFiles(_imagesDirectory, "*.png"))
        {
            if (!referencedImages.Contains(Path.GetFileName(image)))
                File.Delete(image);
        }
    }

    private sealed class Record
    {
        public Guid Id { get; set; }
        public ClipboardItemKind Kind { get; set; }
        public string? Text { get; set; }
        public string[]? Files { get; set; }
        public string? ImageFile { get; set; }
        public DateTimeOffset CapturedAt { get; set; }
        public bool IsPinned { get; set; }
        public string? SourceApplication { get; set; }
        public string? Signature { get; set; }
    }
}
