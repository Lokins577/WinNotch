using System.IO;
using System.Text.Json;
using WinNotch.Scratchpad;

namespace WinNotch.Platform.Windows.Scratchpad;

public sealed class JsonScratchpadStore : IScratchpadStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public JsonScratchpadStore(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WinNotch", "Scratchpad", "notes.json"));
    }

    public IReadOnlyList<ScratchpadDocument> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var documents = JsonSerializer.Deserialize<List<ScratchpadDocument>>(
                File.ReadAllText(_path), Options) ?? [];
            foreach (var document in documents) ScratchpadPolicy.Normalize(document);
            return documents.OrderByDescending(document => document.UpdatedAt).ToArray();
        }
        catch { return []; }
    }

    public void Save(IReadOnlyList<ScratchpadDocument> documents)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (directory == null) throw new InvalidOperationException("Scratchpad path has no directory.");
        Directory.CreateDirectory(directory);
        foreach (var document in documents) ScratchpadPolicy.Normalize(document);
        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(documents, Options));
        File.Move(temporary, _path, overwrite: true);
    }
}
