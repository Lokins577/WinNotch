namespace WinNotch.Features;

public readonly record struct FeatureId
{
    public string Value { get; }

    public FeatureId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > 80 || value[0] == '.' || value[^1] == '.'
            || value.Contains("..", StringComparison.Ordinal)
            || value.Any(character => !(character is >= 'a' and <= 'z'
                or >= '0' and <= '9' or '.' or '-')))
        {
            throw new ArgumentException(
                "Feature IDs must use lowercase letters, digits, dots, and dashes.",
                nameof(value));
        }

        Value = value;
    }

    public override string ToString() => Value;

    public static implicit operator FeatureId(string value) => new(value);
}
