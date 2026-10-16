using System.Globalization;
using System.Text.RegularExpressions;

namespace WinNotch.CommandBar;

public sealed record UnitConversion(double Input, string From, double Output, string To)
{
    public string Display => $"{Input:G} {From} = {Output:G10} {To}";
}

public static partial class UnitConverter
{
    private static readonly Dictionary<string, Unit> Units = CreateUnits();

    public static bool TryConvert(string? query, out UnitConversion? conversion)
    {
        conversion = null;
        if (string.IsNullOrWhiteSpace(query) || query.Length > 120) return false;
        var match = ConversionPattern().Match(query.Trim());
        if (!match.Success
            || !double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double input)) return false;
        string fromName = match.Groups[2].Value.ToLowerInvariant();
        string toName = match.Groups[3].Value.ToLowerInvariant();
        if (!Units.TryGetValue(fromName, out var from)
            || !Units.TryGetValue(toName, out var to)
            || from.Family != to.Family) return false;
        double baseValue = from.ToBase(input);
        conversion = new UnitConversion(input, fromName, to.FromBase(baseValue), toName);
        return double.IsFinite(conversion.Output);
    }

    [GeneratedRegex(@"^\s*([-+]?\d+(?:\.\d+)?)\s*([a-zA-Z]+)\s+(?:to|in)\s+([a-zA-Z]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConversionPattern();

    private static Dictionary<string, Unit> CreateUnits()
    {
        var units = new Dictionary<string, Unit>(StringComparer.OrdinalIgnoreCase);
        void Linear(string family, double factor, params string[] names)
        {
            var unit = new Unit(family, value => value * factor, value => value / factor);
            foreach (string name in names) units[name] = unit;
        }

        Linear("length", 0.001, "mm");
        Linear("length", 0.01, "cm");
        Linear("length", 1, "m", "meter", "meters");
        Linear("length", 1000, "km");
        Linear("length", 0.0254, "in", "inch", "inches");
        Linear("length", 0.3048, "ft", "foot", "feet");
        Linear("length", 0.9144, "yd", "yard", "yards");
        Linear("length", 1609.344, "mi", "mile", "miles");

        Linear("mass", 0.001, "g");
        Linear("mass", 1, "kg");
        Linear("mass", 0.028349523125, "oz");
        Linear("mass", 0.45359237, "lb", "lbs");

        Linear("duration", 0.001, "ms");
        Linear("duration", 1, "s", "sec");
        Linear("duration", 60, "min", "mins");
        Linear("duration", 3600, "h", "hr", "hrs");
        Linear("duration", 86400, "day", "days");

        Linear("data", 1, "b", "byte", "bytes");
        Linear("data", 1000, "kb");
        Linear("data", 1_000_000, "mb");
        Linear("data", 1_000_000_000, "gb");
        Linear("data", 1024, "kib");
        Linear("data", 1_048_576, "mib");
        Linear("data", 1_073_741_824, "gib");

        units["c"] = new Unit("temperature", value => value, value => value);
        units["f"] = new Unit("temperature", value => (value - 32) * 5 / 9, value => value * 9 / 5 + 32);
        units["k"] = new Unit("temperature", value => value - 273.15, value => value + 273.15);
        return units;
    }

    private sealed record Unit(
        string Family,
        Func<double, double> ToBase,
        Func<double, double> FromBase);
}
