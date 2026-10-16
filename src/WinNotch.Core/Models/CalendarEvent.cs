using System;

namespace WinNotch.Models;

/// <summary>
/// A calendar event with title, time, and color.
/// </summary>
public class CalendarEvent
{
    public string Title { get; set; } = string.Empty;
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string CalendarColor { get; set; } = "#4A90D9";
    public bool IsAllDay { get; set; }

    public string TimeDisplay => IsAllDay ? "All day" : Start.ToString("HH:mm");
}
