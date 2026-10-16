namespace WinNotch.Clipboard;

public static class UrlCleaner
{
    private static readonly HashSet<string> TrackingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "dclid", "msclkid", "igshid", "mc_cid", "mc_eid",
        "vero_conv", "vero_id", "wickedid", "yclid", "_hsenc", "_hsmi"
    };

    public static string Clean(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            return value;

        var kept = new List<string>();
        foreach (var pair in uri.Query.TrimStart('?')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string encodedKey = pair.Split('=', 2)[0];
            string key = Uri.UnescapeDataString(encodedKey.Replace('+', ' '));
            if (key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
                || TrackingKeys.Contains(key))
                continue;
            kept.Add(pair);
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Join('&', kept)
        };
        return builder.Uri.AbsoluteUri;
    }
}
