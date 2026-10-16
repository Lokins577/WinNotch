using System.IO;
using System.Text.Json;
using WinNotch.Models;
using WinNotch.Features;
using WinNotch.Platform.Windows.Settings;
using Xunit;

namespace WinNotch.Tests;

public sealed class JsonAppSettingsStoreTests
{
    [Fact]
    public void SaveAndLoad_PreservesExplicitFalseAndZeroValues()
    {
        using var directory = new TempSettingsDirectory();
        var store = new JsonAppSettingsStore(directory.Path, () => null);
        var expected = new AppSettings
        {
            AdaptToWindowsTheme = false,
            HoverCloseDelayMs = 0,
            ShowCalendar = false,
            Theme = AppTheme.Light
        };
        expected.SetFeatureAvailable(BuiltInFeatures.Media, false);

        store.Save(expected);
        var actual = store.Load();

        Assert.False(actual.AdaptToWindowsTheme);
        Assert.Equal(0, actual.HoverCloseDelayMs);
        Assert.False(actual.ShowCalendar);
        Assert.Equal(AppTheme.Light, actual.Theme);
        Assert.False(actual.IsFeatureAvailable(BuiltInFeatures.Media));
    }

    [Fact]
    public void Load_LegacyAutoTheme_MigratesToWindowsAdaptation()
    {
        using var directory = new TempSettingsDirectory();
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(
            System.IO.Path.Combine(directory.Path, "settings.json"),
            JsonSerializer.Serialize(new { Theme = "Auto" }));

        var settings = new JsonAppSettingsStore(directory.Path, () => null).Load();

        Assert.True(settings.AdaptToWindowsTheme);
        Assert.Equal(AppTheme.Dark, settings.Theme);
    }

    [Fact]
    public void Load_InvalidJson_ReturnsDefaults()
    {
        using var directory = new TempSettingsDirectory();
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "settings.json"), "not json");

        var settings = new JsonAppSettingsStore(directory.Path, () => null).Load();

        Assert.True(settings.AutoCheckForUpdates);
        Assert.Equal(HoverMode.HoverPeekClickOpen, settings.HoverMode);
    }

    private sealed class TempSettingsDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "winnotch-settings-tests",
            Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
