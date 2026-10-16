namespace WinNotch.CommandBar;

public enum CommandResultKind
{
    Application,
    Window,
    Clipboard,
    Calculator,
    UnitConversion,
    Emoji,
    File,
    Snippet,
    Script,
    Command
}

public sealed record CommandResult(
    string Id,
    string Title,
    string? Subtitle,
    CommandResultKind Kind,
    double Score,
    Func<CancellationToken, ValueTask>? Execute = null);

public interface ICommandProvider
{
    string Id { get; }
    ValueTask<IReadOnlyList<CommandResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken);
}

public sealed class CommandBarEngine
{
    private readonly IReadOnlyList<ICommandProvider> _providers;

    public CommandBarEngine(IEnumerable<ICommandProvider> providers)
    {
        _providers = providers?.ToArray() ?? throw new ArgumentNullException(nameof(providers));
        if (_providers.Select(provider => provider.Id).Distinct(StringComparer.Ordinal).Count()
            != _providers.Count)
            throw new ArgumentException("Command provider IDs must be unique.", nameof(providers));
    }

    public async Task<IReadOnlyList<CommandResult>> SearchAsync(
        string? query,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        string normalized = query?.Trim() ?? string.Empty;
        var tasks = _providers.Select(provider => SearchProviderAsync(provider, normalized, cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.SelectMany(result => result)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();
    }

    private static async Task<IReadOnlyList<CommandResult>> SearchProviderAsync(
        ICommandProvider provider,
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch { return []; }
    }
}

public static class CommandScoring
{
    public static double Score(string candidate, string query, double baseScore = 0)
    {
        if (string.IsNullOrWhiteSpace(query)) return baseScore;
        if (candidate.Equals(query, StringComparison.CurrentCultureIgnoreCase)) return baseScore + 1000;
        if (candidate.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return baseScore + 700;
        int index = candidate.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        if (index >= 0) return baseScore + 500 - Math.Min(index, 100);

        int cursor = 0;
        foreach (char character in candidate)
        {
            if (cursor < query.Length
                && char.ToUpperInvariant(character) == char.ToUpperInvariant(query[cursor]))
                cursor++;
        }
        return cursor == query.Length ? baseScore + 250 : double.NegativeInfinity;
    }
}
