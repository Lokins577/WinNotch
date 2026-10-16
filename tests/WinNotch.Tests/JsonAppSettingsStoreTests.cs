using System.IO;
using System.Text.Json;
using WinNotch.Models;
using WinNotch.Features;
using WinNotch.Timers;
using WinNotch.Platform.Windows.Settings;
using WinNotch.QuickTools;
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
        expected.TimerPreferences.FocusMinutes = 42;
        expected.TimerSession.Initialized = true;
        expected.TimerSession.Phase = TimerPhase.Stopwatch;
        expected.TimerSession.StoredValue = TimeSpan.FromSeconds(90);
        expected.DownloadsFolder = @"D:\Downloads";
        expected.PreferredMediaSourceId = "spotify.exe";
        expected.OnlineLyricsEnabled = true;
        expected.ClipboardPreferences.MaximumItems = 250;
        expected.ClipboardPreferences.IgnoredApplications.Add("password-manager");
        expected.ClipboardPreferences.AutoClearSeconds = 120;
        expected.ClipboardPreferences.PlainTextPasteHotkey = true;
        expected.ClipboardPreferences.AutoCleanUrls = true;
        expected.CommandBarSearchFolders.Add(@"D:\Projects");
        expected.CommandSnippets.Add(new WinNotch.CommandBar.CommandSnippet
        {
            Name = "Greeting",
            Trigger = "hi",
            Content = "Hello"
        });
        expected.CommandScripts.Add(new WinNotch.CommandBar.SavedCommandScript
        {
            Name = "Build",
            Executable = @"C:\Tools\build.cmd"
        });
        expected.RadialMenu.Items =
        [
            new RadialMenuItem
            {
                Label = "Docs",
                Kind = RadialActionKind.Url,
                Target = "https://example.com/docs"
            }
        ];
        expected.SystemMonitorPreferences.SampleIntervalSeconds = 5;
        expected.SystemMonitorPreferences.SustainedSeconds = 30;
        expected.SystemMonitorPreferences.CpuAlertPercent = 85;
        expected.AudioControlPreferences.PreferredVisualizerOutputId = "output-a";
        expected.AudioControlPreferences.OutputDevicePriority.AddRange(["output-a", "output-b"]);
        expected.CapturePreferences.Format = WinNotch.Capture.CaptureImageFormat.Jpeg;
        expected.CapturePreferences.JpegQuality = 81;
        expected.CapturePreferences.CopyAfterCapture = false;
        expected.RecordingPreferences.FramesPerSecond = 12;
        expected.RecordingPreferences.CaptureMicrophone = true;
        expected.AppManagementPreferences.TemporaryFileMinimumAgeDays = 14;
        expected.AppManagementPreferences.MessagingRetentionDays = 60;
        expected.AppManagementPreferences.MessagingDownloadFolders.Add(@"D:\Chat Downloads");
        expected.InputEnhancementPreferences.ReverseVerticalScroll = true;
        expected.InputEnhancementPreferences.XButton1Action = WinNotch.InputEnhancements.SideButtonAction.MiddleClick;
        expected.InputEnhancementPreferences.ExcludedApplications.Add("game");
        expected.InputEnhancementPreferences.TextExpansions.Add(new WinNotch.InputEnhancements.TextExpansionSnippet
        {
            Trigger = ":sig",
            Replacement = "Regards"
        });

        store.Save(expected);
        var actual = store.Load();

        Assert.False(actual.AdaptToWindowsTheme);
        Assert.Equal(0, actual.HoverCloseDelayMs);
        Assert.False(actual.ShowCalendar);
        Assert.Equal(AppTheme.Light, actual.Theme);
        Assert.False(actual.IsFeatureAvailable(BuiltInFeatures.Media));
        Assert.Equal(42, actual.TimerPreferences.FocusMinutes);
        Assert.Equal(TimerPhase.Stopwatch, actual.TimerSession.Phase);
        Assert.Equal(TimeSpan.FromSeconds(90), actual.TimerSession.StoredValue);
        Assert.Equal(@"D:\Downloads", actual.DownloadsFolder);
        Assert.Equal("spotify.exe", actual.PreferredMediaSourceId);
        Assert.True(actual.OnlineLyricsEnabled);
        Assert.Equal(250, actual.ClipboardPreferences.MaximumItems);
        Assert.Contains("password-manager", actual.ClipboardPreferences.IgnoredApplications);
        Assert.Equal(120, actual.ClipboardPreferences.AutoClearSeconds);
        Assert.True(actual.ClipboardPreferences.PlainTextPasteHotkey);
        Assert.True(actual.ClipboardPreferences.AutoCleanUrls);
        Assert.Contains(@"D:\Projects", actual.CommandBarSearchFolders);
        Assert.Equal("Greeting", Assert.Single(actual.CommandSnippets).Name);
        Assert.Equal("Build", Assert.Single(actual.CommandScripts).Name);
        var radial = Assert.Single(actual.RadialMenu.Items);
        Assert.Equal("Docs", radial.Label);
        Assert.Equal(RadialActionKind.Url, radial.Kind);
        Assert.Equal("https://example.com/docs", radial.Target);
        Assert.Equal(5, actual.SystemMonitorPreferences.SampleIntervalSeconds);
        Assert.Equal(30, actual.SystemMonitorPreferences.SustainedSeconds);
        Assert.Equal(85, actual.SystemMonitorPreferences.CpuAlertPercent);
        Assert.Equal("output-a", actual.AudioControlPreferences.PreferredVisualizerOutputId);
        Assert.Equal(["output-a", "output-b"], actual.AudioControlPreferences.OutputDevicePriority);
        Assert.Equal(WinNotch.Capture.CaptureImageFormat.Jpeg, actual.CapturePreferences.Format);
        Assert.Equal(81, actual.CapturePreferences.JpegQuality);
        Assert.False(actual.CapturePreferences.CopyAfterCapture);
        Assert.Equal(12, actual.RecordingPreferences.FramesPerSecond);
        Assert.True(actual.RecordingPreferences.CaptureMicrophone);
        Assert.Equal(14, actual.AppManagementPreferences.TemporaryFileMinimumAgeDays);
        Assert.Equal(60, actual.AppManagementPreferences.MessagingRetentionDays);
        Assert.Contains(@"D:\Chat Downloads", actual.AppManagementPreferences.MessagingDownloadFolders);
        Assert.True(actual.InputEnhancementPreferences.ReverseVerticalScroll);
        Assert.Equal(WinNotch.InputEnhancements.SideButtonAction.MiddleClick, actual.InputEnhancementPreferences.XButton1Action);
        Assert.Contains("game", actual.InputEnhancementPreferences.ExcludedApplications);
        Assert.Equal(":sig", Assert.Single(actual.InputEnhancementPreferences.TextExpansions).Trigger);
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

    [Fact]
    public void Load_NullRadialMenu_MigratesToDefaults()
    {
        using var directory = new TempSettingsDirectory();
        Directory.CreateDirectory(directory.Path);
        File.WriteAllText(
            System.IO.Path.Combine(directory.Path, "settings.json"),
            "{\"RadialMenu\":null}");

        var settings = new JsonAppSettingsStore(directory.Path, () => null).Load();

        Assert.Equal(6, settings.RadialMenu.Items.Count);
        Assert.Contains(settings.RadialMenu.Items,
            item => item.Kind == RadialActionKind.CommandBar);
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
