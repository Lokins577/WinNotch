using System.Text.Json;
using System.IO;
using WinNotch.Models;
using WinNotch.Settings;
using WinNotch.Platform.Windows.Settings;

namespace WinNotch.Platform.Windows.ProductHardening;

public sealed class SettingsTransferService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly IAppSettingsStore _store;

    public SettingsTransferService(IAppSettingsStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public string Export(AppSettings settings, string path)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var envelope = new SettingsEnvelope(
            1,
            DateTimeOffset.UtcNow,
            System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
            settings);
        string temporary = fullPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(envelope, Options));
        File.Move(temporary, fullPath, overwrite: true);
        return fullPath;
    }

    public AppSettings Import(string path)
    {
        string fullPath = Path.GetFullPath(path);
        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        AppSettings settings;
        if (document.RootElement.TryGetProperty(nameof(SettingsEnvelope.Settings), out var settingsElement))
        {
            int format = document.RootElement.TryGetProperty(nameof(SettingsEnvelope.FormatVersion), out var value)
                ? value.GetInt32() : 0;
            if (format > 1) throw new InvalidOperationException($"Settings export format {format} is newer than this WinNotch build.");
            settings = settingsElement.Deserialize<AppSettings>(Options)
                       ?? throw new InvalidDataException("The settings payload is empty.");
        }
        else
        {
            settings = document.RootElement.Deserialize<AppSettings>(Options)
                       ?? throw new InvalidDataException("The settings file is empty.");
        }
        if (settings.SettingsSchemaVersion > 1)
            throw new InvalidOperationException($"Settings schema {settings.SettingsSchemaVersion} is newer than this WinNotch build.");
        JsonAppSettingsStore.Normalize(settings);
        _store.Save(settings);
        return settings;
    }

    private sealed record SettingsEnvelope(
        int FormatVersion,
        DateTimeOffset ExportedAt,
        string ApplicationVersion,
        AppSettings Settings);
}
