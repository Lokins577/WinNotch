namespace WinNotch.MediaTools;

public static class RecordingTimeline
{
    public static IReadOnlyList<RecordingFrame> SelectFrames(
        RecordingProject project,
        RecordingEditPlan plan)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(plan);
        plan.Normalize(project.Duration);
        TimeSpan end = plan.TrimEnd ?? project.Duration;
        return project.Frames
            .Where(frame => frame.Timestamp >= plan.TrimStart && frame.Timestamp <= end)
            .Where(frame => !plan.Cuts.Any(cut => cut.Contains(frame.Timestamp)))
            .OrderBy(frame => frame.Timestamp)
            .ToArray();
    }

    public static IReadOnlyList<MediaTimeRange> GetKeptRanges(
        TimeSpan duration,
        RecordingEditPlan plan)
    {
        plan.Normalize(duration);
        TimeSpan cursor = plan.TrimStart;
        TimeSpan end = plan.TrimEnd ?? duration;
        var result = new List<MediaTimeRange>();
        foreach (var cut in Merge(plan.Cuts))
        {
            if (cut.Start > cursor) result.Add(new MediaTimeRange(cursor, cut.Start));
            if (cut.End > cursor) cursor = cut.End;
        }
        if (cursor < end) result.Add(new MediaTimeRange(cursor, end));
        return result;
    }

    private static IEnumerable<MediaTimeRange> Merge(IEnumerable<MediaTimeRange> ranges)
    {
        MediaTimeRange? current = null;
        foreach (var range in ranges.OrderBy(value => value.Start))
        {
            if (current == null) current = range;
            else if (range.Start <= current.Value.End)
                current = new MediaTimeRange(current.Value.Start, TimeSpan.FromTicks(Math.Max(current.Value.End.Ticks, range.End.Ticks)));
            else
            {
                yield return current.Value;
                current = range;
            }
        }
        if (current != null) yield return current.Value;
    }
}
