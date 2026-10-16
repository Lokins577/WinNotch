using WinNotch.Music;
using WinNotch.Services;
using Xunit;

namespace WinNotch.Tests;

public sealed class MediaServiceTests
{
    [Fact]
    public void SelectSource_PersistsExplicitAndAutomaticChoices()
    {
        string? persisted = "unchanged";
        using var service = new MediaService(
            preferredSourceId: null,
            persistPreferredSource: value => persisted = value);

        service.SelectSource("spotify.exe");
        Assert.Equal("spotify.exe", persisted);

        service.SelectSource(null);
        Assert.Null(persisted);
        Assert.Equal(MediaQueueCapability.Unsupported, service.QueueCapability);
    }
}
