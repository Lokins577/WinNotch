using WinNotch.Clipboard;
using Xunit;

namespace WinNotch.Tests;

public sealed class UrlCleanerTests
{
    [Fact]
    public void Clean_RemovesTrackingAndPreservesUsefulQueryAndFragment()
    {
        string cleaned = UrlCleaner.Clean(
            "https://example.com/page?id=42&utm_source=newsletter&fbclid=secret#section");

        Assert.Equal("https://example.com/page?id=42#section", cleaned);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("file:///C:/test.txt")]
    public void Clean_LeavesUnsupportedValuesAlone(string value)
    {
        Assert.Equal(value, UrlCleaner.Clean(value));
    }

    [Fact]
    public void Clean_RemovesAllUtmVariantsCaseInsensitively()
    {
        string cleaned = UrlCleaner.Clean("https://example.com/?UTM_Medium=x&utm_campaign=y&keep=yes");

        Assert.Equal("https://example.com/?keep=yes", cleaned);
    }
}
