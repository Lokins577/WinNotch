using WinNotch.Features;
using WinNotch.Models;
using Xunit;

namespace WinNotch.Tests;

public sealed class FeatureRuntimeTests
{
    [Fact]
    public void FeatureId_RejectsUnstableFormats()
    {
        Assert.Throws<ArgumentException>(() => new FeatureId("Island.Media"));
        Assert.Throws<ArgumentException>(() => new FeatureId("island/media"));
        Assert.Throws<ArgumentException>(() => new FeatureId("island..media"));
        Assert.Equal("island.media", new FeatureId("island.media").Value);
    }

    [Fact]
    public void Catalog_RejectsDuplicateIds()
    {
        var first = new FeatureDescriptor(
            "test.feature", "First", FeatureGroup.Tools, "First", true);
        var second = first with { DisplayName = "Second" };

        Assert.Throws<ArgumentException>(() => new FeatureCatalog(new[] { first, second }));
    }

    [Fact]
    public void Catalog_RejectsMissingAndCyclicDependencies()
    {
        var missing = new FeatureDescriptor(
            "test.feature", "Feature", FeatureGroup.Tools, "Feature", true,
            Dependencies: new[] { new FeatureId("missing.feature") });
        Assert.Throws<ArgumentException>(() => new FeatureCatalog(new[] { missing }));

        var first = new FeatureDescriptor(
            "test.first", "First", FeatureGroup.Tools, "First", true,
            Dependencies: new[] { new FeatureId("test.second") });
        var second = new FeatureDescriptor(
            "test.second", "Second", FeatureGroup.Tools, "Second", true,
            Dependencies: new[] { new FeatureId("test.first") });
        Assert.Throws<ArgumentException>(() => new FeatureCatalog(new[] { first, second }));
    }

    [Fact]
    public void Settings_UseCatalogDefaultUntilExplicitlyOverridden()
    {
        var settings = new AppSettings();

        Assert.True(settings.IsFeatureAvailable(BuiltInFeatures.Media));
        Assert.False(settings.IsFeatureAvailable(BuiltInFeatures.Notifications));

        settings.SetFeatureAvailable(BuiltInFeatures.Media, false);
        settings.SetFeatureAvailable(BuiltInFeatures.Notifications, true);

        Assert.False(settings.IsFeatureAvailable(BuiltInFeatures.Media));
        Assert.True(settings.IsFeatureAvailable(BuiltInFeatures.Notifications));
    }

    [Fact]
    public void Runtime_RejectsPlannedFeatureRegistration()
    {
        var runtime = new FeatureRuntime(BuiltInFeatures.Catalog);

        Assert.Throws<InvalidOperationException>(() =>
            runtime.Register(BuiltInFeatures.InputEnhancements.Id, () => new FakeModule(BuiltInFeatures.InputEnhancements.Id)));
    }

    [Fact]
    public async Task Runtime_StartStop_IsIdempotentAndRestartable()
    {
        var created = new List<FakeModule>();
        await using var runtime = new FeatureRuntime(BuiltInFeatures.Catalog);
        runtime.Register(BuiltInFeatures.Island.Id, () => new FakeModule(BuiltInFeatures.Island.Id));
        runtime.Register(BuiltInFeatures.Media.Id, () =>
        {
            var module = new FakeModule(BuiltInFeatures.Media.Id);
            created.Add(module);
            return module;
        });

        await runtime.SetEnabledAsync(BuiltInFeatures.Media.Id, true);
        await runtime.SetEnabledAsync(BuiltInFeatures.Media.Id, true);

        Assert.Single(created);
        Assert.Equal(1, created[0].StartCount);
        Assert.Equal(FeatureRuntimeState.Running, runtime.GetState(BuiltInFeatures.Media.Id).State);

        await runtime.SetEnabledAsync(BuiltInFeatures.Media.Id, false);
        await runtime.SetEnabledAsync(BuiltInFeatures.Media.Id, false);

        Assert.Equal(1, created[0].StopCount);
        Assert.Equal(1, created[0].DisposeCount);
        Assert.Equal(FeatureRuntimeState.Stopped, runtime.GetState(BuiltInFeatures.Media.Id).State);

        await runtime.SetEnabledAsync(BuiltInFeatures.Media.Id, true);
        Assert.Equal(2, created.Count);
        Assert.Equal(1, created[1].StartCount);
    }

    [Fact]
    public async Task Runtime_StartFailure_IsObservableAndCanBeStopped()
    {
        var failure = new InvalidOperationException("start failed");
        var module = new FakeModule(BuiltInFeatures.Media.Id, startFailure: failure);
        await using var runtime = new FeatureRuntime(BuiltInFeatures.Catalog);
        runtime.Register(BuiltInFeatures.Island.Id, () => new FakeModule(BuiltInFeatures.Island.Id));
        runtime.Register(BuiltInFeatures.Media.Id, () => module);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.SetEnabledAsync(BuiltInFeatures.Media.Id, true));

        Assert.Same(failure, thrown);
        var snapshot = runtime.GetState(BuiltInFeatures.Media.Id);
        Assert.Equal(FeatureRuntimeState.Faulted, snapshot.State);
        Assert.Same(failure, snapshot.Failure);

        await runtime.SetEnabledAsync(BuiltInFeatures.Media.Id, false);
        Assert.Equal(FeatureRuntimeState.Stopped, runtime.GetState(BuiltInFeatures.Media.Id).State);
        Assert.Equal(1, module.DisposeCount);
    }

    [Fact]
    public async Task Runtime_StartsDependenciesAndPreventsPrematureStop()
    {
        await using var runtime = new FeatureRuntime(BuiltInFeatures.Catalog);
        runtime.Register(BuiltInFeatures.Island.Id, () => new FakeModule(BuiltInFeatures.Island.Id));
        runtime.Register(BuiltInFeatures.Media.Id, () => new FakeModule(BuiltInFeatures.Media.Id));

        await runtime.SetEnabledAsync(BuiltInFeatures.Media.Id, true);

        Assert.Equal(FeatureRuntimeState.Running, runtime.GetState(BuiltInFeatures.Island.Id).State);
        Assert.Equal(FeatureRuntimeState.Running, runtime.GetState(BuiltInFeatures.Media.Id).State);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await runtime.SetEnabledAsync(BuiltInFeatures.Island.Id, false));

        await runtime.SetEnabledAsync(BuiltInFeatures.Media.Id, false);
        await runtime.SetEnabledAsync(BuiltInFeatures.Island.Id, false);
        Assert.Equal(FeatureRuntimeState.Stopped, runtime.GetState(BuiltInFeatures.Island.Id).State);
    }

    private sealed class FakeModule : IFeatureModule
    {
        private readonly Exception? _startFailure;

        public FakeModule(FeatureId id, Exception? startFailure = null)
        {
            Id = id;
            _startFailure = startFailure;
        }

        public FeatureId Id { get; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            return _startFailure == null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(_startFailure);
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
