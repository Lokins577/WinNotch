using System.Text.RegularExpressions;

namespace WinNotch.AppManagement;

public static partial class WingetOutputParser
{
    public static IReadOnlyList<PackageUpgrade> ParseUpgrades(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return [];
        string[] lines = output.Replace("\r", string.Empty).Split('\n');
        int separator = Array.FindIndex(lines, line => line.Count(character => character == '-') >= 8
            && line.Trim().All(character => character == '-' || char.IsWhiteSpace(character)));
        if (separator < 0) return [];
        var result = new List<PackageUpgrade>();
        foreach (string line in lines.Skip(separator + 1))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('-')) continue;
            Match row = UpgradeRow().Match(trimmed);
            if (row.Success)
            {
                result.Add(new PackageUpgrade(
                    row.Groups["name"].Value.Trim(),
                    row.Groups["id"].Value,
                    row.Groups["installed"].Value,
                    row.Groups["available"].Value,
                    row.Groups["source"].Value));
                continue;
            }
            string[] values = ColumnSeparator().Split(trimmed);
            if (values.Length < 4) continue;
            string source = values.Length >= 5 ? values[^1] : string.Empty;
            int offset = values.Length >= 5 ? values.Length - 5 : 0;
            string name = string.Join(" ", values.Take(offset + 1));
            string id = values[offset + 1];
            string installed = values[offset + 2];
            string available = values[offset + 3];
            if (name.Length == 0 || id.Length == 0 || available.Length == 0) continue;
            result.Add(new PackageUpgrade(name, id, installed, available, source));
        }
        return result;
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ColumnSeparator();

    [GeneratedRegex(@"^(?<name>.+?)\s+(?<id>[A-Za-z0-9][A-Za-z0-9._+-]{1,199})\s+(?<installed>(?:<\s*)?\S+)\s+(?<available>\S+)\s+(?<source>winget|msstore)$", RegexOptions.IgnoreCase)]
    private static partial Regex UpgradeRow();
}
