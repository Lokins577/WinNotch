using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Music;

namespace WinNotch.Views;

public partial class MusicPlayerView : UserControl
{
    private MediaService? _mediaService;
    private MediaQueueService? _mediaQueueService;
    private LyricsService? _lyricsService;
    private MediaQueueWindow? _queueWindow;
    private bool _updatingSources;

    public MusicPlayerView()
    {
        InitializeComponent();
    }

    public void Bind(
        MediaService mediaService,
        LyricsService lyricsService,
        MediaQueueService mediaQueueService)
    {
        _mediaService = mediaService;
        _lyricsService = lyricsService;
        _mediaQueueService = mediaQueueService;
        var info = mediaService.MediaInfo;

        info.PropertyChanged += (_, e) =>
        {
            Dispatcher.Invoke(() => UpdateUI(info, e.PropertyName));
        };

        mediaService.SessionChanged += () =>
        {
            Dispatcher.Invoke(() => UpdateAll(info));
        };
        mediaService.SourcesChanged += () => Dispatcher.Invoke(UpdateSources);
        lyricsService.Updated += snapshot => Dispatcher.Invoke(() => UpdateLyrics(snapshot));
        mediaQueueService.Updated += snapshot => Dispatcher.Invoke(() => UpdateQueue(snapshot));

        UpdateAll(info);
        UpdateSources();
        UpdateLyrics(lyricsService.Snapshot);
        UpdateQueue(mediaQueueService.Snapshot);
    }

    private void UpdateAll(MediaInfo info)
    {
        UpdateUI(info, null);
    }

    private void UpdateUI(MediaInfo info, string? propertyName)
    {
        // Visibility
        if (!info.HasMedia)
        {
            NoMediaText.Visibility = Visibility.Visible;
            AlbumArtGrid.Visibility = Visibility.Collapsed;
            InfoControlsGrid.Visibility = Visibility.Collapsed;
            return;
        }
        NoMediaText.Visibility = Visibility.Collapsed;
        AlbumArtGrid.Visibility = Visibility.Visible;
        InfoControlsGrid.Visibility = Visibility.Visible;

        if (propertyName == null || propertyName == nameof(info.Title))
            TitleText.Text = info.Title;

        if (propertyName == null || propertyName == nameof(info.Artist))
            ArtistText.Text = info.Artist;

        if (propertyName == null || propertyName == nameof(info.AlbumArt))
        {
            AlbumArtImage.Source = info.AlbumArt;
            GlowBrush.ImageSource = info.AlbumArt;
        }

        if (propertyName == null || propertyName == nameof(info.IsPlaying))
            PlayPauseIcon.Text = info.IsPlaying ? "⏸" : "▶";

        if (propertyName == null || propertyName == nameof(info.Position) || propertyName == nameof(info.ProgressPercent))
        {
            ElapsedText.Text = FormatTime(info.Position);
            UpdateProgressBar(info.ProgressPercent);
            UpdateLyrics(_lyricsService?.Snapshot);
        }

        if (propertyName == null || propertyName == nameof(info.Duration))
            DurationText.Text = FormatTime(info.Duration);

        if (propertyName == null || propertyName == nameof(info.IsShuffle))
            ShuffleButton.Opacity = info.IsShuffle ? 1.0 : 0.4;

        if (propertyName == null || propertyName == nameof(info.RepeatMode))
        {
            RepeatIcon.Text = info.RepeatMode == 2 ? "🔂" : "🔁";
            RepeatButton.Opacity = info.RepeatMode > 0 ? 1.0 : 0.4;
        }

        if (propertyName == null || propertyName == nameof(info.DominantColor))
        {
            var color = info.DominantColor;
            var accentBrush = new SolidColorBrush(color);
            accentBrush.Freeze();
            ProgressFill.Background = accentBrush;
        }
    }

    private void UpdateQueue(MediaQueueSnapshot snapshot)
    {
        QueueButton.Visibility = snapshot.Capability == MediaQueueCapability.Unsupported
            ? Visibility.Collapsed
            : Visibility.Visible;
        QueueButton.ToolTip = snapshot.Message
                              ?? $"{snapshot.Provider} queue ({snapshot.Queue.Count} item(s))";
    }

    private void OnQueueClick(object sender, RoutedEventArgs e)
    {
        if (_mediaQueueService == null) return;
        if (_queueWindow is { IsVisible: true })
        {
            _queueWindow.Activate();
            return;
        }
        _queueWindow = new MediaQueueWindow(_mediaQueueService)
        {
            Owner = Window.GetWindow(this),
            Topmost = true
        };
        _queueWindow.Closed += (_, _) => _queueWindow = null;
        _queueWindow.Show();
        _queueWindow.Topmost = false;
    }

    private void UpdateSources()
    {
        if (_mediaService == null) return;
        _updatingSources = true;
        SourceCombo.ItemsSource = _mediaService.Sources;
        SourceCombo.SelectedValue = _mediaService.MediaInfo.SourceId;
        SourceCombo.Visibility = _mediaService.Sources.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        _updatingSources = false;
    }

    private void UpdateLyrics(LyricsSnapshot? snapshot)
    {
        if (snapshot == null || _lyricsService == null)
        {
            LyricsText.Text = string.Empty;
            return;
        }

        var line = _lyricsService.GetCurrentLine(_mediaService?.MediaInfo.Position ?? TimeSpan.Zero);
        LyricsText.Text = line?.Text ?? snapshot.Message ?? snapshot.Status switch
        {
            LyricsStatus.Loading => "Looking up lyrics…",
            LyricsStatus.Idle => string.Empty,
            _ => string.Empty
        };
    }

    private void OnSourceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSources || _mediaService == null) return;
        _mediaService.SelectSource(SourceCombo.SelectedValue as string);
    }

    private void UpdateProgressBar(double percent)
    {
        if (ProgressFill.Parent is Border parent)
        {
            double parentWidth = parent.ActualWidth;
            // Only update if parent has valid width to prevent visual glitches
            if (parentWidth > 0 && !double.IsNaN(parentWidth) && !double.IsInfinity(parentWidth))
            {
                double targetWidth = Math.Max(0, Math.Min(percent * parentWidth, parentWidth));
                ProgressFill.Width = targetWidth;
            }
        }
    }

    private static string FormatTime(TimeSpan ts)
    {
        return ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    // Transport button handlers
    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_mediaService != null) await _mediaService.PlayPauseAsync();
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_mediaService != null) await _mediaService.NextAsync();
    }

    private async void OnPrevClick(object sender, RoutedEventArgs e)
    {
        if (_mediaService != null) await _mediaService.PreviousAsync();
    }

    private async void OnShuffleClick(object sender, RoutedEventArgs e)
    {
        if (_mediaService != null) await _mediaService.ToggleShuffleAsync();
    }

    private async void OnRepeatClick(object sender, RoutedEventArgs e)
    {
        if (_mediaService != null) await _mediaService.CycleRepeatAsync();
    }

    private async void OnProgressBarClick(object sender, MouseButtonEventArgs e)
    {
        if (_mediaService == null || sender is not Border bar) return;

        double barWidth = bar.ActualWidth;
        // Validate bar width before calculating seek position
        if (barWidth <= 0 || double.IsNaN(barWidth) || double.IsInfinity(barWidth)) return;

        var pos = e.GetPosition(bar);
        double percent = Math.Clamp(pos.X / barWidth, 0, 1);
        await _mediaService.SeekToAsync(percent);
    }
}
