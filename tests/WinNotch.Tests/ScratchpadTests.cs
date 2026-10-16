using System.IO;
using WinNotch.Platform.Windows.Scratchpad;
using WinNotch.Scratchpad;
using WinNotch.Services;
using Xunit;

namespace WinNotch.Tests;

public sealed class ScratchpadTests
{
    [Fact]
    public void Store_RoundTripsDocumentsAtomically()
    {
        using var directory = new TempScratchpadDirectory();
        string path = System.IO.Path.Combine(directory.Path, "notes.json");
        var store = new JsonScratchpadStore(path);
        var document = new ScratchpadDocument { Title = "Plan", Content = "# Heading" };

        store.Save([document]);
        var restored = store.Load();

        Assert.Equal("Plan", Assert.Single(restored).Title);
        Assert.Equal("# Heading", restored[0].Content);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Service_AlwaysKeepsAtLeastOneDocument()
    {
        var store = new MemoryStore();
        using var service = new ScratchpadService(store);
        var document = Assert.Single(service.Documents);
        service.Update(document.Id, "Title", "Content");
        Assert.Equal("Content", service.Documents[0].Content);

        Assert.True(service.Remove(document.Id));
        Assert.Single(service.Documents);
    }

    private sealed class MemoryStore : IScratchpadStore
    {
        public IReadOnlyList<ScratchpadDocument> Load() => [];
        public void Save(IReadOnlyList<ScratchpadDocument> documents) { }
    }

    private sealed class TempScratchpadDirectory : IDisposable
    {
        public TempScratchpadDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "winnotch-scratchpad-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
