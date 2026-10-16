namespace WinNotch.Features;

public sealed class FeatureRuntime : IAsyncDisposable
{
    private readonly FeatureCatalog _catalog;
    private readonly Dictionary<FeatureId, Entry> _entries = new();

    public FeatureRuntime(FeatureCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public event Action<FeatureRuntimeSnapshot>? StateChanged;

    public void Register(FeatureId id, Func<IFeatureModule> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var descriptor = _catalog.Get(id);

        if (descriptor.Maturity is FeatureMaturity.Planned or FeatureMaturity.Unsupported)
            throw new InvalidOperationException($"Feature {id} is not available for registration.");
        if (!_entries.TryAdd(id, new Entry(descriptor, factory)))
            throw new InvalidOperationException($"Feature {id} is already registered.");
    }

    public FeatureRuntimeSnapshot GetState(FeatureId id)
    {
        var entry = GetEntry(id);
        return entry.Snapshot;
    }

    public async ValueTask SetEnabledAsync(
        FeatureId id,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var entry = GetEntry(id);
        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (enabled)
                await StartAsync(entry, cancellationToken).ConfigureAwait(false);
            else
                await StopAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _entries.Values.OrderByDescending(entry => GetDependencyDepth(entry.Descriptor)))
        {
            await entry.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await StopAsync(entry, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                entry.Gate.Release();
            }
            entry.Gate.Dispose();
        }
    }

    private int GetDependencyDepth(FeatureDescriptor feature)
    {
        var dependencies = feature.Dependencies ?? Array.Empty<FeatureId>();
        return dependencies.Count == 0
            ? 0
            : 1 + dependencies.Max(dependency => GetDependencyDepth(_catalog.Get(dependency)));
    }

    private Entry GetEntry(FeatureId id)
    {
        return _entries.TryGetValue(id, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Feature {id} has no registered module.");
    }

    private async ValueTask StartAsync(Entry entry, CancellationToken cancellationToken)
    {
        if (entry.State == FeatureRuntimeState.Running)
            return;
        if (entry.State is FeatureRuntimeState.Starting or FeatureRuntimeState.Stopping)
            throw new InvalidOperationException($"Feature {entry.Descriptor.Id} is transitioning.");

        foreach (var dependency in entry.Descriptor.Dependencies ?? Array.Empty<FeatureId>())
            await SetEnabledAsync(dependency, true, cancellationToken).ConfigureAwait(false);

        entry.Module ??= entry.Factory();
        if (entry.Module.Id != entry.Descriptor.Id)
            throw new InvalidOperationException(
                $"Feature factory returned {entry.Module.Id} for {entry.Descriptor.Id}.");

        entry.SetState(FeatureRuntimeState.Starting, null);
        Publish(entry);
        try
        {
            await entry.Module.StartAsync(cancellationToken).ConfigureAwait(false);
            entry.SetState(FeatureRuntimeState.Running, null);
            Publish(entry);
        }
        catch (Exception exception)
        {
            entry.SetState(FeatureRuntimeState.Faulted, exception);
            Publish(entry);
            throw;
        }
    }

    private async ValueTask StopAsync(Entry entry, CancellationToken cancellationToken)
    {
        if (entry.Module == null || entry.State == FeatureRuntimeState.Stopped)
            return;

        var dependent = _entries.Values.FirstOrDefault(candidate =>
            candidate.State == FeatureRuntimeState.Running
            && (candidate.Descriptor.Dependencies ?? Array.Empty<FeatureId>()).Contains(entry.Descriptor.Id));
        if (dependent != null)
            throw new InvalidOperationException(
                $"Feature {entry.Descriptor.Id} is required by running feature {dependent.Descriptor.Id}.");

        entry.SetState(FeatureRuntimeState.Stopping, null);
        Publish(entry);
        try
        {
            await entry.Module.StopAsync(cancellationToken).ConfigureAwait(false);
            await entry.Module.DisposeAsync().ConfigureAwait(false);
            entry.Module = null;
            entry.SetState(FeatureRuntimeState.Stopped, null);
            Publish(entry);
        }
        catch (Exception exception)
        {
            entry.SetState(FeatureRuntimeState.Faulted, exception);
            Publish(entry);
            throw;
        }
    }

    private void Publish(Entry entry)
    {
        var snapshot = entry.Snapshot;
        foreach (Action<FeatureRuntimeSnapshot> handler in StateChanged?.GetInvocationList()
                     ?? Array.Empty<Delegate>())
        {
            try { handler(snapshot); }
            catch { }
        }
    }

    private sealed class Entry
    {
        public FeatureDescriptor Descriptor { get; }
        public Func<IFeatureModule> Factory { get; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public IFeatureModule? Module { get; set; }
        public FeatureRuntimeState State { get; private set; } = FeatureRuntimeState.Stopped;
        public Exception? Failure { get; private set; }
        public FeatureRuntimeSnapshot Snapshot => new(Descriptor, State, Failure);

        public Entry(FeatureDescriptor descriptor, Func<IFeatureModule> factory)
        {
            Descriptor = descriptor;
            Factory = factory;
        }

        public void SetState(FeatureRuntimeState state, Exception? failure)
        {
            State = state;
            Failure = failure;
        }
    }
}
