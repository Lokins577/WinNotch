using System.IO;
using WinNotch.Clipboard;
using WinNotch.Platform.Windows.Clipboard;
using Xunit;

namespace WinNotch.Tests;

public sealed class ClipboardHistoryTests
{
    [Fact]
    public void Add_DeduplicatesAndPreservesPin()
    {
        var history = new ClipboardHistory(new ClipboardPreferences());
        var first = Text("hello", DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.True(history.Add(first));
        Assert.True(history.SetPinned(first.Id, true));

        var duplicate = Text("hello", DateTimeOffset.UtcNow);
        Assert.True(history.Add(duplicate));

        var item = Assert.Single(history.Items);
        Assert.Equal(first.Id, item.Id);
        Assert.True(item.IsPinned);
        Assert.Equal(duplicate.CapturedAt, item.CapturedAt);
    }

    [Fact]
    public void Capacity_TrimsOldestUnpinnedButKeepsPinned()
    {
        var preferences = new ClipboardPreferences { MaximumItems = 10 };
        var history = new ClipboardHistory(preferences);
        var pinned = Text("pinned", DateTimeOffset.UtcNow.AddDays(-1));
        history.Add(pinned);
        history.SetPinned(pinned.Id, true);
        for (int index = 0; index < 14; index++)
            history.Add(Text($"item-{index}", DateTimeOffset.UtcNow.AddMinutes(index)));

        Assert.Equal(11, history.Items.Count);
        Assert.Contains(history.Items, item => item.Id == pinned.Id);
        Assert.DoesNotContain(history.Items, item => item.Text == "item-0");
    }

    [Fact]
    public void Search_MatchesTextAndFileNames()
    {
        var history = new ClipboardHistory(new ClipboardPreferences());
        history.Add(Text("release checklist", DateTimeOffset.UtcNow));
        history.Add(new ClipboardHistoryItem
        {
            Kind = ClipboardItemKind.Files,
            Files = [@"D:\build\report.pdf"],
            CapturedAt = DateTimeOffset.UtcNow
        });

        Assert.Equal("release checklist", Assert.Single(history.Search("check")).Text);
        Assert.Equal(ClipboardItemKind.Files, Assert.Single(history.Search("report")).Kind);
    }

    [Fact]
    public void JsonStore_RoundTripsAllSupportedKinds()
    {
        using var directory = new TempClipboardDirectory();
        var store = new JsonClipboardHistoryStore(directory.Path);
        var items = new ClipboardHistoryItem[]
        {
            Text("text", DateTimeOffset.UtcNow),
            new()
            {
                Kind = ClipboardItemKind.Files,
                Files = [@"C:\one.txt", @"C:\two.txt"],
                CapturedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Kind = ClipboardItemKind.Image,
                ImagePng = [1, 2, 3, 4],
                CapturedAt = DateTimeOffset.UtcNow,
                IsPinned = true
            }
        };
        foreach (var item in items) item.Signature = ClipboardHistory.ComputeSignature(item);

        store.Save(items);
        var restored = store.Load();

        Assert.Equal(3, restored.Count);
        Assert.Equal("text", restored.Single(item => item.Kind == ClipboardItemKind.Text).Text);
        Assert.Equal(2, restored.Single(item => item.Kind == ClipboardItemKind.Files).Files.Length);
        Assert.Equal([1, 2, 3, 4], restored.Single(item => item.Kind == ClipboardItemKind.Image).ImagePng);
    }

    private static ClipboardHistoryItem Text(string text, DateTimeOffset capturedAt)
        => new() { Kind = ClipboardItemKind.Text, Text = text, CapturedAt = capturedAt };

    private sealed class TempClipboardDirectory : IDisposable
    {
        public TempClipboardDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "winnotch-clipboard-tests", Guid.NewGuid().ToString("N"));
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
