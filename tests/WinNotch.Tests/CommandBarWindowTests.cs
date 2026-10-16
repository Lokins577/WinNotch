using WinNotch.CommandBar;
using WinNotch.Views;
using WinNotch.Models;
using WinNotch.Settings;
using WinNotch.Scratchpad;
using WinNotch.Services;
using Xunit;

namespace WinNotch.Tests;

public sealed class CommandBarWindowTests
{
    [Fact]
    public void Constructor_BuildsOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new CommandBarWindow(new CommandBarEngine([]));
                Assert.NotNull(window.Content);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Command Bar UI test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void SettingsWindow_BuildsOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new CommandBarSettingsWindow(
                    new AppSettings(), new MemoryStore(), () => { });
                Assert.NotNull(window.Content);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Command Bar settings UI test timed out.");
        Assert.Null(failure);
    }

    private sealed class MemoryStore : IAppSettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
        public void ApplyStartOnBoot(bool enabled) { }
    }

    [Fact]
    public void QuickPanelAndScratchpad_BuildOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var quick = new QuickPanelWindow(new QuickPanelActions(
                    () => { }, () => { }, () => { }, () => { }, () => { }, () => { }));
                using var service = new ScratchpadService(new MemoryScratchpadStore());
                var scratchpad = new ScratchpadWindow(service);
                Assert.NotNull(quick.Content);
                Assert.NotNull(scratchpad.Content);
                quick.Close();
                scratchpad.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Quick tools UI test timed out.");
        Assert.Null(failure);
    }

    private sealed class MemoryScratchpadStore : IScratchpadStore
    {
        public IReadOnlyList<ScratchpadDocument> Load() => [];
        public void Save(IReadOnlyList<ScratchpadDocument> documents) { }
    }
}
