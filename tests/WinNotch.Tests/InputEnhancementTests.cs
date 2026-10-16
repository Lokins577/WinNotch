using WinNotch.InputEnhancements;
using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Settings;
using WinNotch.Views;
using Xunit;

namespace WinNotch.Tests;

public sealed class InputEnhancementTests
{
    [Fact]
    public void ScrollTransformer_PreservesResidualAndDirection()
    {
        var preferences = new InputEnhancementPreferences
        {
            ScrollMultiplier = 0.1,
            ReverseVerticalScroll = true
        };
        var transformer = new ScrollDeltaTransformer();

        Assert.Equal(-12, transformer.Transform(120, horizontal: false, preferences));
        preferences.ScrollMultiplier = 0.333;
        int total = Enumerable.Range(0, 10)
            .Sum(_ => transformer.Transform(1, horizontal: false, preferences));

        Assert.Equal(-3, total);
    }

    [Fact]
    public void TextExpansion_UsesLongestSuffixOnlyAtDelimiter()
    {
        var snippets = new List<TextExpansionSnippet>
        {
            new() { Trigger = ":a", Replacement = "short" },
            new() { Trigger = ":addr", Replacement = "123 Main Street" }
        };
        var engine = new TextExpansionEngine(() => snippets);
        foreach (char value in "hello :addr") Assert.Null(engine.Accept(value));

        var match = engine.Accept(' ');

        Assert.NotNull(match);
        Assert.Equal(":addr", match!.Trigger);
        Assert.Equal("123 Main Street", match.Replacement);
    }

    [Fact]
    public void Preferences_NormalizeAndStripExeExclusions()
    {
        var preferences = new InputEnhancementPreferences
        {
            ScrollMultiplier = 99,
            FocusDelayMilliseconds = 1,
            ExcludedApplications = [" game.exe ", "GAME"],
            TextExpansions =
            [
                new TextExpansionSnippet { Trigger = " x ", Replacement = "one" },
                new TextExpansionSnippet { Trigger = "x", Replacement = "two" }
            ]
        };

        preferences.Normalize();

        Assert.Equal(5, preferences.ScrollMultiplier);
        Assert.Equal(50, preferences.FocusDelayMilliseconds);
        Assert.Equal(["game"], preferences.ExcludedApplications);
        Assert.Single(preferences.TextExpansions);
        Assert.True(preferences.IsExcluded("GAME"));
    }

    [Fact]
    public void HookLifecycle_StartsAndStopsWithAllRewritesDisabled()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var service = new InputEnhancementFeatureService(new InputEnhancementPreferences());
                service.StartAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
                service.StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Input hook lifecycle test timed out.");
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
                var settings = new AppSettings();
                using var service = new InputEnhancementFeatureService(settings.InputEnhancementPreferences);
                var window = new InputEnhancementSettingsWindow(settings, new MemoryStore(), service);
                Assert.NotNull(window.Content);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Input settings UI test timed out.");
        Assert.Null(failure);
    }

    private sealed class MemoryStore : IAppSettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
        public void ApplyStartOnBoot(bool enabled) { }
    }
}
