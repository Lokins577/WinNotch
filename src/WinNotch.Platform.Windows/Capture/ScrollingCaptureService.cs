using System.Drawing;
using System.Windows.Automation;

namespace WinNotch.Platform.Windows.Capture;

public sealed class ScrollingCaptureService
{
    private readonly WindowsCaptureService _capture;

    public ScrollingCaptureService(WindowsCaptureService capture)
        => _capture = capture ?? throw new ArgumentNullException(nameof(capture));

    public async Task<Bitmap> CaptureAsync(IntPtr windowHandle, CancellationToken cancellationToken = default)
    {
        if (!WindowsCaptureService.TryGetWindowBounds(windowHandle, out _))
            throw new InvalidOperationException("The foreground window is unavailable.");
        var root = AutomationElement.FromHandle(windowHandle);
        var element = FindScrollable(root)
                      ?? throw new NotSupportedException("The foreground window exposes no vertically scrollable UI Automation element.");
        var pattern = (ScrollPattern)element.GetCurrentPattern(ScrollPattern.Pattern);
        if (!pattern.Current.VerticallyScrollable)
            throw new NotSupportedException("The selected UI Automation element cannot scroll vertically.");
        var rect = element.Current.BoundingRectangle;
        var bounds = Rectangle.FromLTRB(
            (int)Math.Round(rect.Left), (int)Math.Round(rect.Top),
            (int)Math.Round(rect.Right), (int)Math.Round(rect.Bottom));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException("The scrollable element has no visible capture bounds.");

        double original = pattern.Current.VerticalScrollPercent;
        var frames = new List<Bitmap>();
        try
        {
            pattern.SetScrollPercent(ScrollPattern.NoScroll, 0);
            await Task.Delay(180, cancellationToken);
            double view = Math.Clamp(pattern.Current.VerticalViewSize, 1, 100);
            double step = Math.Max(5, view * 0.85);
            double position = 0;
            for (int index = 0; index < 30; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                frames.Add(_capture.CaptureRegion(bounds));
                if (position >= 100 - view || pattern.Current.VerticalScrollPercent >= 99.9) break;
                position = Math.Min(100, position + step);
                pattern.SetScrollPercent(ScrollPattern.NoScroll, position);
                await Task.Delay(180, cancellationToken);
            }
            return Stitch(frames, overlapFraction: 0.15);
        }
        finally
        {
            try { pattern.SetScrollPercent(ScrollPattern.NoScroll, original); }
            catch { }
            foreach (var frame in frames) frame.Dispose();
        }
    }

    private static AutomationElement? FindScrollable(AutomationElement root)
    {
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        int inspected = 0;
        while (queue.Count > 0 && inspected++ < 200)
        {
            var current = queue.Dequeue();
            try
            {
                if (current.TryGetCurrentPattern(ScrollPattern.Pattern, out var value)
                    && ((ScrollPattern)value).Current.VerticallyScrollable) return current;
                var child = TreeWalker.ControlViewWalker.GetFirstChild(current);
                while (child != null)
                {
                    queue.Enqueue(child);
                    child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                }
            }
            catch { }
        }
        return null;
    }

    private static Bitmap Stitch(IReadOnlyList<Bitmap> frames, double overlapFraction)
    {
        if (frames.Count == 0) throw new InvalidOperationException("No scrolling frames were captured.");
        int overlap = (int)Math.Round(frames[0].Height * overlapFraction);
        int appendHeight = Math.Max(1, frames[0].Height - overlap);
        var result = new Bitmap(
            frames[0].Width,
            frames[0].Height + appendHeight * (frames.Count - 1));
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImageUnscaled(frames[0], 0, 0);
        for (int index = 1; index < frames.Count; index++)
        {
            var source = new Rectangle(0, overlap, frames[index].Width, frames[index].Height - overlap);
            var target = new Rectangle(0, frames[0].Height + appendHeight * (index - 1), source.Width, source.Height);
            graphics.DrawImage(frames[index], target, source, GraphicsUnit.Pixel);
        }
        return result;
    }
}
