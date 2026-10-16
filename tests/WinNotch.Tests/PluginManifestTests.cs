using System.Text.Json;
using WinNotch.Plugins;
using Xunit;

namespace WinNotch.Tests;

public class PluginManifestTests
{
    [Theory]
    [InlineData("WinNotch.Models.CalendarEvent", "WinNotch.Core")]
    [InlineData("WinNotch.Models.AppSettings", "WinNotch.Core")]
    [InlineData("WinNotch.Models.AppTheme", "WinNotch.Core")]
    [InlineData("WinNotch.Models.HoverMode", "WinNotch.Core")]
    [InlineData("WinNotch.Models.MediaInfo", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Models.NotchState", "WinNotch.PluginSdk")]
    [InlineData("WinNotch.Models.NotchDock", "WinNotch.Core")]
    [InlineData("WinNotch.Models.ShelfItem", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Plugins.PluginLibraryService", "WinNotch.PluginHost")]
    [InlineData("WinNotch.Plugins.PluginManager", "WinNotch.PluginHost")]
    [InlineData("WinNotch.Plugins.PluginManifest", "WinNotch.PluginSdk")]
    [InlineData("WinNotch.Plugins.IPlugin", "WinNotch.PluginSdk")]
    [InlineData("WinNotch.Plugins.IPluginContext", "WinNotch.PluginSdk")]
    [InlineData("WinNotch.Plugins.IUIPlugin", "WinNotch.PluginSdk")]
    [InlineData("WinNotch.Plugins.PluginBase", "WinNotch.PluginSdk")]
    [InlineData("WinNotch.Plugins.UIPluginLocation", "WinNotch.PluginSdk")]
    [InlineData("WinNotch.Services.AudioCaptureService", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Services.BatteryService", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Services.BrightnessService", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Services.CalendarService", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Services.ColorExtractionService", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Services.FullscreenService", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Services.MediaService", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Services.ShelfService", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Services.ThemePalette", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Services.ThemeService", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Services.UpdateCheckResult", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Services.UpdateService", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Services.VolumeService", "WinNotch.Platform.Windows")]
    [InlineData("WinNotch.Services.WebcamService", "WinNotch.Platform.Windows")]
    public void LegacyRuntimeTypeName_ResolvesThroughForwarder(string typeName, string expectedAssembly)
    {
        var forwarded = Type.GetType($"{typeName}, WinNotch", throwOnError: true);

        Assert.NotNull(forwarded);
        Assert.Equal(expectedAssembly, forwarded!.Assembly.GetName().Name);
    }

    [Theory]
    [InlineData("Fun", PluginCategory.Fun)]
    [InlineData("fun", PluginCategory.Fun)]
    [InlineData("System Utility", PluginCategory.SystemUtility)]
    [InlineData("Widget", PluginCategory.Widget)]
    public void DeserializingKnownCategoryAliases_UsesExpectedEnum(string rawCategory, PluginCategory expectedCategory)
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(
            $$"""
            {
              "id": "demo.plugin",
              "name": "Demo Plugin",
              "version": "1.0.0",
              "author": "WinNotch Team",
              "description": "Test plugin",
              "downloadUrl": "https://example.com/plugin.dll",
              "homepage": "https://example.com",
              "category": "{{rawCategory}}"
            }
            """);

        Assert.NotNull(manifest);
        Assert.Equal(expectedCategory, manifest!.Category);
    }

    [Fact]
    public void DeserializingUnknownCategory_FallsBackToOther()
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(
            """
            {
              "id": "demo.plugin",
              "name": "Demo Plugin",
              "version": "1.0.0",
              "author": "WinNotch Team",
              "description": "Test plugin",
              "downloadUrl": "https://example.com/plugin.dll",
              "homepage": "https://example.com",
              "category": "UnexpectedCategory"
            }
            """);

        Assert.NotNull(manifest);
        Assert.Equal(PluginCategory.Other, manifest!.Category);
    }
}
