using WinNotch.Views;
using WinNotch.WindowManagement;
using System.Windows.Threading;
using Xunit;

namespace WinNotch.Tests;

public sealed class WindowManagementTests
{
    private static readonly WindowBounds WorkArea = new(10, 20, 1001, 801);

    [Theory]
    [InlineData(WindowLayoutPreset.LeftHalf, 10, 20, 500, 801)]
    [InlineData(WindowLayoutPreset.RightHalf, 510, 20, 501, 801)]
    [InlineData(WindowLayoutPreset.TopLeft, 10, 20, 500, 400)]
    [InlineData(WindowLayoutPreset.BottomRight, 510, 420, 501, 401)]
    [InlineData(WindowLayoutPreset.LeftThird, 10, 20, 333, 801)]
    [InlineData(WindowLayoutPreset.CenterThird, 343, 20, 333, 801)]
    [InlineData(WindowLayoutPreset.RightThird, 676, 20, 335, 801)]
    [InlineData(WindowLayoutPreset.Maximize, 10, 20, 1001, 801)]
    public void LayoutPlanner_PartitionsMonitorWorkArea(
        WindowLayoutPreset preset,
        int left,
        int top,
        int width,
        int height)
    {
        Assert.Equal(new WindowBounds(left, top, width, height),
            WindowLayoutPlanner.Calculate(WorkArea, preset));
    }

    [Fact]
    public void LayoutPlanner_RejectsEmptyWorkArea()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindowLayoutPlanner.Calculate(new WindowBounds(0, 0, 0, 100),
                WindowLayoutPreset.LeftHalf));
    }

    [Fact]
    public void WindowsBackend_ReadOnlyEnumerationExcludesWinNotchProcess()
    {
        using var backend = new WindowsWindowManagementBackend();

        var windows = backend.GetWindows(currentVirtualDesktopOnly: false);

        Assert.DoesNotContain(windows, window => window.ProcessId == Environment.ProcessId);
        Assert.All(windows, window =>
        {
            Assert.NotEqual(0, window.Handle);
            Assert.False(string.IsNullOrWhiteSpace(window.Title));
            Assert.False(window.Bounds.IsEmpty);
        });
    }

    [Fact]
    public void WindowsBackend_RefusesToQuitOwnProcess()
    {
        using var backend = new WindowsWindowManagementBackend();

        var result = backend.RequestQuit(Environment.ProcessId);

        Assert.False(result.Success);
    }

    [Fact]
    public void Switcher_BuildsOnStaThreadWithoutEnumeratingRealWindows()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(() =>
            {
                try
                {
                    using var backend = new FakeBackend();
                    var window = new WindowSwitcherWindow(backend);
                    Assert.NotNull(window.Content);
                    window.Show();
                    window.UpdateLayout();
                    window.Close();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Window switcher UI test timed out.");
        Assert.Null(failure);
    }

    private sealed class FakeBackend : IWindowManagementBackend
    {
        public IReadOnlyList<ManagedWindow> GetWindows(bool currentVirtualDesktopOnly = true) =>
        [
            new ManagedWindow(
                0, 7, "Document", "Editor", new WindowBounds(0, 0, 800, 600),
                "DISPLAY1", false, true)
        ];

        public WindowOperationResult Activate(long handle) => Done();
        public WindowOperationResult ApplyLayout(long handle, WindowLayoutPreset preset) => Done();
        public WindowOperationResult Restore(long handle) => Done();
        public WindowOperationResult MoveToAdjacentMonitor(long handle, int direction) => Done();
        public WindowOperationResult RequestClose(long handle) => Done();
        public WindowOperationResult RequestQuit(int processId) => Done();
        public void Dispose() { }
        private static WindowOperationResult Done() => WindowOperationResult.Completed("done");
    }
}
