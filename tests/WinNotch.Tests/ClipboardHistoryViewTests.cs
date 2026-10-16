using System.Windows.Controls;
using WinNotch.Clipboard;
using WinNotch.Services;
using WinNotch.Views;
using Xunit;

namespace WinNotch.Tests;

public sealed class ClipboardHistoryViewTests
{
    [Fact]
    public void View_RendersPersistedItemOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var item = new ClipboardHistoryItem
                {
                    Kind = ClipboardItemKind.Text,
                    Text = "saved text",
                    CapturedAt = DateTimeOffset.UtcNow
                };
                item.Signature = ClipboardHistory.ComputeSignature(item);
                using var service = new ClipboardHistoryService(
                    new ClipboardPreferences(),
                    new MemoryStore([item]));
                var view = new ClipboardHistoryView();
                view.Bind(service);

                var host = Assert.IsType<StackPanel>(view.FindName("ItemHost"));
                Assert.Single(host.Children);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Clipboard UI test timed out.");
        Assert.Null(failure);
    }

    private sealed class MemoryStore(IReadOnlyList<ClipboardHistoryItem> items) : IClipboardHistoryStore
    {
        public IReadOnlyList<ClipboardHistoryItem> Load() => items;
        public void Save(IReadOnlyList<ClipboardHistoryItem> value) { }
    }
}
