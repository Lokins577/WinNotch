using System.Windows;
using System.Windows.Controls;
using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Settings;
using WinNotch.Views;
using Xunit;

namespace WinNotch.Tests;

public sealed class TimerViewTests
{
    [Fact]
    public void Views_RenderSharedTimerStateOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                using var service = new TimerFeatureService(settings, new MemorySettingsStore());
                var expanded = new TimerView();
                var compact = new TimerCompactView();
                expanded.Bind(service);
                compact.Bind(service);
                service.Start();

                service.SelectStopwatch();
                service.Toggle();

                var phase = Assert.IsType<TextBlock>(expanded.FindName("PhaseText"));
                Assert.Equal("Stopwatch", phase.Text);
                Assert.Equal(Visibility.Visible, compact.Visibility);
                service.Stop();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Timer UI test timed out.");

        Assert.Null(failure);
    }

    private sealed class MemorySettingsStore : IAppSettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
        public void ApplyStartOnBoot(bool enabled) { }
    }
}
