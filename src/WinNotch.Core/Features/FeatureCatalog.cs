namespace WinNotch.Features;

public sealed class FeatureCatalog
{
    private readonly Dictionary<FeatureId, FeatureDescriptor> _features;

    public FeatureCatalog(IEnumerable<FeatureDescriptor> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        var entries = new Dictionary<FeatureId, FeatureDescriptor>();
        foreach (var feature in features)
        {
            ArgumentNullException.ThrowIfNull(feature);
            if (!entries.TryAdd(feature.Id, feature))
                throw new ArgumentException($"Duplicate feature ID: {feature.Id}", nameof(features));
        }

        foreach (var feature in entries.Values)
        {
            foreach (var dependency in feature.Dependencies ?? Array.Empty<FeatureId>())
            {
                if (!entries.ContainsKey(dependency))
                    throw new ArgumentException(
                        $"Feature {feature.Id} depends on unknown feature {dependency}.",
                        nameof(features));
                if (dependency == feature.Id)
                    throw new ArgumentException(
                        $"Feature {feature.Id} cannot depend on itself.",
                        nameof(features));
            }
        }

        EnsureAcyclic(entries);

        _features = entries;
    }

    public IReadOnlyCollection<FeatureDescriptor> Features => _features.Values;

    public FeatureDescriptor Get(FeatureId id)
    {
        return _features.TryGetValue(id, out var feature)
            ? feature
            : throw new KeyNotFoundException($"Unknown feature: {id}");
    }

    public bool TryGet(FeatureId id, out FeatureDescriptor? feature)
    {
        return _features.TryGetValue(id, out feature);
    }

    private static void EnsureAcyclic(IReadOnlyDictionary<FeatureId, FeatureDescriptor> features)
    {
        var visiting = new HashSet<FeatureId>();
        var visited = new HashSet<FeatureId>();

        foreach (var feature in features.Values)
            Visit(feature);

        void Visit(FeatureDescriptor feature)
        {
            if (visited.Contains(feature.Id)) return;
            if (!visiting.Add(feature.Id))
                throw new ArgumentException($"Feature dependency cycle includes {feature.Id}.", nameof(features));

            foreach (var dependency in feature.Dependencies ?? Array.Empty<FeatureId>())
                Visit(features[dependency]);

            visiting.Remove(feature.Id);
            visited.Add(feature.Id);
        }
    }
}
