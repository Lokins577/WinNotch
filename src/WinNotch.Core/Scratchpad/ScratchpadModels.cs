namespace WinNotch.Scratchpad;

public sealed class ScratchpadDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Note";
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public interface IScratchpadStore
{
    IReadOnlyList<ScratchpadDocument> Load();
    void Save(IReadOnlyList<ScratchpadDocument> documents);
}

public static class ScratchpadPolicy
{
    public static void Normalize(ScratchpadDocument document)
    {
        document.Title = string.IsNullOrWhiteSpace(document.Title)
            ? "Note"
            : document.Title.Trim()[..Math.Min(document.Title.Trim().Length, 80)];
        if (document.Content.Length > 500_000)
            document.Content = document.Content[..500_000];
        if (document.UpdatedAt == default)
            document.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
