using System.IO;
using WinNotch.Services;
using Xunit;

namespace WinNotch.Tests;

public sealed class ShelfServiceTests
{
    [Fact]
    public void PinnedItems_RoundTripAndSurviveClear()
    {
        using var directory = new TempShelfDirectory();
        string file = System.IO.Path.Combine(directory.Path, "document.txt");
        File.WriteAllText(file, "content");
        string storage = System.IO.Path.Combine(directory.Path, "shelf.json");
        var service = new ShelfService(storage);
        service.AddFiles([file]);
        var item = Assert.Single(service.Items);
        service.SetPinned(item, true);

        var restored = new ShelfService(storage);
        var pinned = Assert.Single(restored.Items);
        Assert.True(pinned.IsPinned);
        Assert.False(pinned.IsMissing);

        restored.AddFiles([System.IO.Path.Combine(directory.Path, "temporary.txt")]);
        restored.Clear();
        Assert.Single(restored.Items);
        Assert.True(restored.Items[0].IsPinned);
    }

    [Fact]
    public void MissingPinnedPath_RemainsVisibleAsMissing()
    {
        using var directory = new TempShelfDirectory();
        string file = System.IO.Path.Combine(directory.Path, "gone.txt");
        File.WriteAllText(file, "content");
        string storage = System.IO.Path.Combine(directory.Path, "shelf.json");
        var service = new ShelfService(storage);
        service.AddFiles([file]);
        service.SetPinned(service.Items[0], true);
        File.Delete(file);

        var restored = new ShelfService(storage);

        Assert.True(Assert.Single(restored.Items).IsMissing);
    }

    private sealed class TempShelfDirectory : IDisposable
    {
        public TempShelfDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "winnotch-shelf-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
