namespace WinNotch.Music;

public enum LyricsStatus
{
    Disabled,
    Idle,
    Loading,
    Available,
    NotFound,
    Failed
}

public sealed record LyricsLine(TimeSpan? Timestamp, string Text);

public sealed record LyricsTrack(
    string Title,
    string Artist,
    string? Album,
    TimeSpan Duration);

public sealed record LyricsResult(
    IReadOnlyList<LyricsLine> Lines,
    bool IsSynced,
    bool IsInstrumental = false);

public sealed record LyricsSnapshot(
    LyricsStatus Status,
    LyricsResult? Result = null,
    string? Message = null);

public static class LrcParser
{
    public static LyricsResult Parse(string? syncedLyrics, string? plainLyrics, bool instrumental = false)
    {
        if (instrumental)
            return new LyricsResult([], false, true);

        var synced = ParseSynced(syncedLyrics)
            .OrderBy(line => line.Timestamp)
            .ToArray();
        if (synced.Length > 0)
            return new LyricsResult(synced, true);

        var plain = (plainLyrics ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => new LyricsLine(null, line))
            .ToArray();
        return new LyricsResult(plain, false);
    }

    private static IEnumerable<LyricsLine> ParseSynced(string? lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyrics)) yield break;

        foreach (var rawLine in lyrics.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            int cursor = 0;
            var timestamps = new List<TimeSpan>();
            while (cursor < rawLine.Length && rawLine[cursor] == '[')
            {
                int end = rawLine.IndexOf(']', cursor + 1);
                if (end < 0) break;
                if (TryParseTimestamp(rawLine.AsSpan(cursor + 1, end - cursor - 1), out var timestamp))
                    timestamps.Add(timestamp);
                cursor = end + 1;
            }

            string text = rawLine[cursor..].Trim();
            if (timestamps.Count == 0 || text.Length == 0) continue;
            foreach (var timestamp in timestamps)
                yield return new LyricsLine(timestamp, text);
        }
    }

    private static bool TryParseTimestamp(ReadOnlySpan<char> value, out TimeSpan timestamp)
    {
        timestamp = TimeSpan.Zero;
        int colon = value.IndexOf(':');
        if (colon <= 0) return false;
        if (!int.TryParse(value[..colon], out int minutes)) return false;
        if (!double.TryParse(value[(colon + 1)..],
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out double seconds)) return false;
        if (minutes < 0 || seconds is < 0 or >= 60) return false;
        timestamp = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return true;
    }
}
