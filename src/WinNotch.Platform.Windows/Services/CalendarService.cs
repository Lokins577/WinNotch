using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WinNotch.Models;

namespace WinNotch.Services;

/// <summary>
/// Provides calendar events. Currently reads from Outlook COM if available,
/// otherwise returns an empty list. Can be extended for Microsoft Graph, .ics files, etc.
/// </summary>
public class CalendarService : IDisposable
{
    public event Action? EventsUpdated;

    public List<CalendarEvent> TodayEvents { get; private set; } = new();

    private System.Windows.Threading.DispatcherTimer? _refreshTimer;
    private int _refreshing;

    public void Initialize()
    {
        RefreshEvents();

        // Refresh every 5 minutes
        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _refreshTimer.Tick += (_, _) => RefreshEvents();
        _refreshTimer.Start();
    }

    public void RefreshEvents()
    {
        // COM interop can take seconds — never block the UI thread, never overlap.
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;

        // Outlook COM prefers an STA apartment; thread-pool threads are MTA.
        var thread = new Thread(() =>
        {
            try
            {
                var events = FetchOutlookEvents();
                TodayEvents = events;
                EventsUpdated?.Invoke();
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        })
        {
            IsBackground = true,
            Name = "WinNotch.CalendarRefresh"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static List<CalendarEvent> FetchOutlookEvents()
    {
        object? outlook = null;
        object? ns = null;
        object? calFolder = null;
        object? items = null;
        object? restricted = null;

        try
        {
            // Attach to a RUNNING Outlook instance only. CreateInstance would
            // silently launch Outlook in the background on every refresh.
            outlook = GetActiveOutlook();
            if (outlook == null) return new List<CalendarEvent>();

            dynamic ol = outlook;
            ns = ol.GetNamespace("MAPI");
            dynamic dns = ns!;
            // olFolderCalendar = 9
            calFolder = dns.GetDefaultFolder(9);
            dynamic dcal = calFolder!;
            items = dcal.Items;
            dynamic ditems = items!;
            ditems.Sort("[Start]");
            ditems.IncludeRecurrences = true;

            var now = DateTime.Now;
            var endOfDay = now.Date.AddDays(1).AddSeconds(-1);

            // Restrict to today's events (Outlook expects locale-formatted dates)
            string filter = $"[Start] >= '{now:g}' AND [Start] <= '{endOfDay:g}'";
            restricted = ditems.Restrict(filter);

            var events = new List<CalendarEvent>();
            foreach (dynamic item in (dynamic)restricted!)
            {
                try
                {
                    events.Add(new CalendarEvent
                    {
                        Title = item.Subject ?? "Untitled",
                        Start = item.Start,
                        End = item.End,
                        IsAllDay = item.AllDayEvent,
                        CalendarColor = "#4A90D9"
                    });
                }
                catch { }
                if (events.Count >= 5) break; // Limit to 5 events
            }
            return events;
        }
        catch
        {
            return new List<CalendarEvent>();
        }
        finally
        {
            ReleaseCom(restricted);
            ReleaseCom(items);
            ReleaseCom(calFolder);
            ReleaseCom(ns);
            ReleaseCom(outlook);
        }
    }

    private static void ReleaseCom(object? com)
    {
        try
        {
            if (com != null && Marshal.IsComObject(com))
                Marshal.ReleaseComObject(com);
        }
        catch { }
    }

    /// <summary>
    /// Equivalent of Marshal.GetActiveObject (removed in .NET Core+):
    /// returns the running Outlook.Application or null if Outlook isn't open.
    /// </summary>
    private static object? GetActiveOutlook()
    {
        try
        {
            if (CLSIDFromProgID("Outlook.Application", out var clsid) != 0)
                return null;

            return GetActiveObject(ref clsid, IntPtr.Zero, out var obj) == 0 ? obj : null;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string lpszProgID, out Guid pclsid);

    [DllImport("oleaut32.dll")]
    private static extern int GetActiveObject(ref Guid rclsid, IntPtr pvReserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    public void Dispose()
    {
        _refreshTimer?.Stop();
    }
}
