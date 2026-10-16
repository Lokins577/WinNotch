namespace WinNotch.WindowManagement;

public static class WindowLayoutPlanner
{
    public static WindowBounds Calculate(WindowBounds workArea, WindowLayoutPreset preset)
    {
        if (workArea.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(workArea), "The monitor work area must be non-empty.");

        int halfWidth = workArea.Width / 2;
        int halfHeight = workArea.Height / 2;
        int thirdWidth = workArea.Width / 3;

        return preset switch
        {
            WindowLayoutPreset.LeftHalf => new(
                workArea.Left, workArea.Top, halfWidth, workArea.Height),
            WindowLayoutPreset.RightHalf => new(
                workArea.Left + halfWidth, workArea.Top,
                workArea.Width - halfWidth, workArea.Height),
            WindowLayoutPreset.TopLeft => new(
                workArea.Left, workArea.Top, halfWidth, halfHeight),
            WindowLayoutPreset.TopRight => new(
                workArea.Left + halfWidth, workArea.Top,
                workArea.Width - halfWidth, halfHeight),
            WindowLayoutPreset.BottomLeft => new(
                workArea.Left, workArea.Top + halfHeight,
                halfWidth, workArea.Height - halfHeight),
            WindowLayoutPreset.BottomRight => new(
                workArea.Left + halfWidth, workArea.Top + halfHeight,
                workArea.Width - halfWidth, workArea.Height - halfHeight),
            WindowLayoutPreset.LeftThird => new(
                workArea.Left, workArea.Top, thirdWidth, workArea.Height),
            WindowLayoutPreset.CenterThird => new(
                workArea.Left + thirdWidth, workArea.Top,
                thirdWidth, workArea.Height),
            WindowLayoutPreset.RightThird => new(
                workArea.Left + thirdWidth * 2, workArea.Top,
                workArea.Width - thirdWidth * 2, workArea.Height),
            WindowLayoutPreset.Maximize => workArea,
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };
    }
}
