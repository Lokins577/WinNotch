namespace WinNotch.InputEnhancements;

public sealed class ScrollDeltaTransformer
{
    private double _verticalResidual;
    private double _horizontalResidual;

    public int Transform(int delta, bool horizontal, InputEnhancementPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        double direction = horizontal
            ? (preferences.ReverseHorizontalScroll ? -1 : 1)
            : (preferences.ReverseVerticalScroll ? -1 : 1);
        double value = delta * preferences.ScrollMultiplier * direction;
        ref double residual = ref (horizontal ? ref _horizontalResidual : ref _verticalResidual);
        value += residual;
        int output = (int)Math.Truncate(value);
        residual = value - output;
        return output;
    }

    public void Reset() => _verticalResidual = _horizontalResidual = 0;
}

public sealed class TextExpansionEngine
{
    private readonly Func<IReadOnlyList<TextExpansionSnippet>> _snippets;
    private readonly List<char> _buffer = [];

    public TextExpansionEngine(Func<IReadOnlyList<TextExpansionSnippet>> snippets)
        => _snippets = snippets ?? throw new ArgumentNullException(nameof(snippets));

    public TextExpansionMatch? Accept(char character)
    {
        if (character == '\b')
        {
            if (_buffer.Count > 0) _buffer.RemoveAt(_buffer.Count - 1);
            return null;
        }
        if (!IsDelimiter(character))
        {
            if (!char.IsControl(character)) _buffer.Add(character);
            Trim();
            return null;
        }

        string typed = new(_buffer.ToArray());
        var match = _snippets()
            .Where(value => typed.EndsWith(value.Trigger, StringComparison.Ordinal))
            .OrderByDescending(value => value.Trigger.Length)
            .FirstOrDefault();
        _buffer.Clear();
        return match == null ? null : new TextExpansionMatch(match.Trigger, match.Replacement, character);
    }

    public void Reset() => _buffer.Clear();

    private void Trim()
    {
        if (_buffer.Count > 128) _buffer.RemoveRange(0, _buffer.Count - 128);
    }

    private static bool IsDelimiter(char value)
        => char.IsWhiteSpace(value) || value is '.' or ',' or '!' or '?' or ')' or ']' or '}';
}
