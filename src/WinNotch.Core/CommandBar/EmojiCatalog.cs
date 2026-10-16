namespace WinNotch.CommandBar;

public static class EmojiCatalog
{
    private static readonly (string Emoji, string[] Keywords)[] Entries =
    [
        ("😀", ["grin", "happy", "smile"]),
        ("😂", ["laugh", "tears", "funny"]),
        ("❤️", ["heart", "love"]),
        ("👍", ["thumbs up", "approve", "yes"]),
        ("👎", ["thumbs down", "disapprove", "no"]),
        ("🎉", ["party", "celebrate", "success"]),
        ("✅", ["check", "done", "complete"]),
        ("❌", ["cross", "error", "no"]),
        ("⚠️", ["warning", "alert"]),
        ("🔥", ["fire", "hot"]),
        ("🚀", ["rocket", "launch", "ship"]),
        ("💡", ["idea", "light"]),
        ("🔍", ["search", "find"]),
        ("📋", ["clipboard", "paste"]),
        ("📁", ["folder", "files"]),
        ("⏱️", ["timer", "stopwatch"]),
        ("🎵", ["music", "song"]),
        ("🔔", ["bell", "notification"]),
        ("🤖", ["robot", "agent", "ai"]),
        ("✨", ["sparkles", "magic", "new"])
    ];

    public static IReadOnlyList<(string Emoji, string Label)> Search(string query, int limit = 10)
    {
        string normalized = query.Trim();
        return Entries
            .Select(entry => (entry, score: entry.Keywords.Max(keyword => CommandScoring.Score(keyword, normalized))))
            .Where(value => value.score > double.NegativeInfinity)
            .OrderByDescending(value => value.score)
            .Take(Math.Clamp(limit, 1, Entries.Length))
            .Select(value => (value.entry.Emoji, string.Join(", ", value.entry.Keywords)))
            .ToArray();
    }
}
