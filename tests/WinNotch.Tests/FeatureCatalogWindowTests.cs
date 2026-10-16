using WinNotch.Models;
using WinNotch.Views;
using Xunit;

namespace WinNotch.Tests;

public sealed class FeatureCatalogWindowTests
{
    [Fact]
    public void Constructor_BuildsCatalogOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new FeatureCatalogWindow(new AppSettings());
                Assert.Equal("WinNotch Features", window.Title);
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Feature catalog UI test timed out.");

        Assert.Null(failure);
    }
}
