namespace WinNotch.Features;

public interface IFeatureModule : IAsyncDisposable
{
    FeatureId Id { get; }
    ValueTask StartAsync(CancellationToken cancellationToken);
    ValueTask StopAsync(CancellationToken cancellationToken);
}

public enum FeatureRuntimeState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

public sealed record FeatureRuntimeSnapshot(
    FeatureDescriptor Feature,
    FeatureRuntimeState State,
    Exception? Failure);
