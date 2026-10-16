using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WinNotch.Helpers;
using Forms = System.Windows.Forms;

namespace WinNotch.Services;

public sealed class SoftwareDimmingService : IDisposable
{
    private readonly Dictionary<string, Window> _windows = new(StringComparer.OrdinalIgnoreCase);
    private double _opacity;

    public SoftwareDimmingService()
        => SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

    public void Apply(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0, 0.9);
        if (_opacity <= 0)
        {
            CloseAll();
            return;
        }
        var active = Forms.Screen.AllScreens.Select(value => value.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string removed in _windows.Keys.Where(key => !active.Contains(key)).ToArray())
        {
            _windows[removed].Close();
            _windows.Remove(removed);
        }
        foreach (Forms.Screen screen in Forms.Screen.AllScreens)
        {
            if (!_windows.TryGetValue(screen.DeviceName, out var window))
            {
                window = CreateWindow(screen);
                _windows[screen.DeviceName] = window;
                window.Show();
            }
            window.Opacity = _opacity;
        }
    }

    private static Window CreateWindow(Forms.Screen screen)
    {
        double scale = ScreenHelper.GetScaleForScreen(screen);
        var window = new Window
        {
            Width = screen.Bounds.Width / scale,
            Height = screen.Bounds.Height / scale,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Background = Brushes.Black,
            ShowActivated = false
        };
        window.SourceInitialized += (_, _) =>
        {
            WindowHelper.SetWindowRectPhysical(
                window, screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height);
            WindowHelper.MakeOverlayWindow(window);
            WindowHelper.SetClickThrough(window, true);
            WindowHelper.SetExcludeFromCapture(window, true);
        };
        return window;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => Application.Current?.Dispatcher.BeginInvoke(() => Apply(_opacity));

    private void CloseAll()
    {
        foreach (var window in _windows.Values) window.Close();
        _windows.Clear();
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        CloseAll();
    }
}
