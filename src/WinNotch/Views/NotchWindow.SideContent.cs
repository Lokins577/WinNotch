using System;
using System.Windows;
using WinNotch.Models;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private void UpdateSideClock()
    {
        var now = DateTime.Now;
        SideTimeText.Text = now.ToString("HH:mm");
        SideDateText.Text = now.ToString("ddd d", System.Globalization.CultureInfo.CurrentCulture);
        SideTimeSmall.Text = now.ToString("HH:mm");
    }

    private void UpdateSideMediaContent(MediaInfo info, string? prop)
    {
        if (prop == null || prop == nameof(info.Title))
            SideSongTitle.Text = info.Title;
        if (prop == null || prop == nameof(info.Artist))
            SideArtistName.Text = info.Artist;
        if (prop == null || prop == nameof(info.AlbumArt))
            SideAlbumArt.Source = info.AlbumArt;
        if (prop == null || prop == nameof(info.IsPlaying))
            SidePlayIcon.Text = info.IsPlaying ? "⏸" : "▶";
        if (prop == null || prop == nameof(info.HasMedia))
            SideMusicPanel.Visibility = info.HasMedia ? Visibility.Visible : Visibility.Collapsed;

        // Progress bar + time
        if (prop == null || prop == nameof(info.Position) || prop == nameof(info.ProgressPercent))
        {
            SideElapsedText.Text = FormatTimeSpan(info.Position);
            UpdateSideProgressBar(info.ProgressPercent);
        }
        if (prop == null || prop == nameof(info.Duration))
            SideDurationText.Text = FormatTimeSpan(info.Duration);

        // Visualizer bar color from album dominant color
        if (prop == null || prop == nameof(info.DominantColor))
        {
            var color = info.DominantColor;
            double brightness = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
            SideVisualizer.BarColor = brightness < 0.3 ? System.Windows.Media.Colors.White : color;
        }
    }

    private void UpdateSideProgressBar(double percent)
    {
        if (SideProgressFill.Parent is System.Windows.Controls.Border parent)
        {
            double parentWidth = parent.ActualWidth;
            // Only update if parent has valid width to prevent visual glitches
            if (parentWidth > 0 && !double.IsNaN(parentWidth) && !double.IsInfinity(parentWidth))
            {
                double targetWidth = Math.Max(0, Math.Min(percent * parentWidth, parentWidth));
                SideProgressFill.Width = targetWidth;
            }
        }
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    private async void OnSidePlayClick(object sender, RoutedEventArgs e) =>
        await _mediaService.PlayPauseAsync();

    private async void OnSidePrevClick(object sender, RoutedEventArgs e) =>
        await _mediaService.PreviousAsync();

    private async void OnSideNextClick(object sender, RoutedEventArgs e) =>
        await _mediaService.NextAsync();

    private async void OnSideProgressBarClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border bar) return;

        double barWidth = bar.ActualWidth;
        // Validate bar width before calculating seek position
        if (barWidth <= 0 || double.IsNaN(barWidth) || double.IsInfinity(barWidth)) return;

        var pos = e.GetPosition(bar);
        double percent = Math.Clamp(pos.X / barWidth, 0, 1);
        await _mediaService.SeekToAsync(percent);
    }

    private void OnSideSettingsClick(object sender, RoutedEventArgs e)
    {
        // In vertical view, expand settings on the right side dynamically
        if (_isSettingsExpanded)
            CollapseSettings();
        else
            ExpandSettings();
    }
}
