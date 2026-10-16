using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WinNotch.Models;
using WinNotch.Music;

namespace WinNotch.Services;

public class MediaService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private readonly DispatcherTimer _positionTimer;
    private DateTime _lastTimelineUpdate = DateTime.MinValue;
    private TimeSpan _basePosition = TimeSpan.Zero;
    private string? _preferredSourceId;
    private readonly Action<string?>? _persistPreferredSource;

    public MediaInfo MediaInfo { get; } = new();
    public IReadOnlyList<MediaSourceInfo> Sources { get; private set; } = [];
    public MediaQueueCapability QueueCapability => MediaQueueCapability.Unsupported;

    public event Action? SessionChanged;
    public event Action? SourcesChanged;

    public MediaService(string? preferredSourceId = null, Action<string?>? persistPreferredSource = null)
    {
        _preferredSourceId = preferredSourceId;
        _persistPreferredSource = persistPreferredSource;
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200) // Update more frequently for smoother progress
        };
        _positionTimer.Tick += OnPositionTimerTick;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            _manager.SessionsChanged += OnSessionsChanged;
            RefreshSourcesAndSession();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MediaService init failed: {ex.Message}");
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            RefreshSourcesAndSession();
        });
    }

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args)
    {
        Application.Current?.Dispatcher.Invoke(RefreshSourcesAndSession);
    }

    public void SelectSource(string? sourceId)
    {
        _preferredSourceId = string.IsNullOrWhiteSpace(sourceId) ? null : sourceId;
        _persistPreferredSource?.Invoke(_preferredSourceId);
        RefreshSourcesAndSession();
    }

    private void RefreshSourcesAndSession()
    {
        if (_manager == null) return;

        var sessions = _manager.GetSessions();
        var target = !string.IsNullOrWhiteSpace(_preferredSourceId)
            ? sessions.FirstOrDefault(session =>
                string.Equals(session.SourceAppUserModelId, _preferredSourceId, StringComparison.OrdinalIgnoreCase))
            : null;
        target ??= _manager.GetCurrentSession();
        AttachSession(target);

        string? currentId = target?.SourceAppUserModelId;
        Sources = sessions
            .Select(session => session.SourceAppUserModelId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new MediaSourceInfo(
                id,
                GetSourceDisplayName(id),
                string.Equals(id, currentId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(source => source.IsCurrent)
            .ThenBy(source => source.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        SourcesChanged?.Invoke();
        SessionChanged?.Invoke();
    }

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        // Detach old
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }

        _session = session;

        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelineChanged;

            MediaInfo.HasMedia = true;
            MediaInfo.SourceId = _session.SourceAppUserModelId ?? string.Empty;
            MediaInfo.SourceDisplayName = GetSourceDisplayName(MediaInfo.SourceId);
            _ = UpdateMediaPropertiesAsync();
            UpdatePlaybackInfo();
            UpdateTimeline();
            _positionTimer.Start();
        }
        else
        {
            MediaInfo.HasMedia = false;
            MediaInfo.IsPlaying = false;
            // Clear stale metadata so nothing from the old session lingers
            MediaInfo.Title = string.Empty;
            MediaInfo.Artist = string.Empty;
            MediaInfo.AlbumTitle = string.Empty;
            MediaInfo.AlbumArt = null;
            MediaInfo.Position = TimeSpan.Zero;
            MediaInfo.Duration = TimeSpan.Zero;
            MediaInfo.SourceId = string.Empty;
            MediaInfo.SourceDisplayName = string.Empty;
            _positionTimer.Stop();
        }
    }

    private static string GetSourceDisplayName(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return "Media";
        string value = sourceId;
        int bang = value.LastIndexOf('!');
        if (bang >= 0 && bang < value.Length - 1)
            value = value[(bang + 1)..];
        value = Path.GetFileNameWithoutExtension(value);
        int underscore = value.IndexOf('_');
        if (underscore > 0) value = value[..underscore];
        return string.IsNullOrWhiteSpace(value) ? sourceId : value;
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        Application.Current?.Dispatcher.Invoke(() => _ = UpdateMediaPropertiesAsync());
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        Application.Current?.Dispatcher.Invoke(UpdatePlaybackInfo);
    }

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        Application.Current?.Dispatcher.Invoke(UpdateTimeline);
    }

    private async Task UpdateMediaPropertiesAsync()
    {
        if (_session == null) return;

        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            if (props == null) return;

            MediaInfo.Title = props.Title ?? string.Empty;
            MediaInfo.Artist = props.Artist ?? string.Empty;
            MediaInfo.AlbumTitle = props.AlbumTitle ?? string.Empty;

            // Load album art
            if (props.Thumbnail != null)
            {
                try
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream.AsStreamForRead();
                    bitmap.DecodePixelWidth = 300;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    MediaInfo.AlbumArt = bitmap;

                    // Extract dominant color
                    MediaInfo.DominantColor = ColorExtractionService.GetDominantColor(bitmap);
                }
                catch
                {
                    MediaInfo.AlbumArt = null;
                }
            }
            else
            {
                MediaInfo.AlbumArt = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateMediaProperties failed: {ex.Message}");
        }
    }

    private void UpdatePlaybackInfo()
    {
        if (_session == null) return;

        try
        {
            var info = _session.GetPlaybackInfo();
            if (info == null) return;

            bool wasPlaying = MediaInfo.IsPlaying;
            MediaInfo.IsPlaying = info.PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // Reset timeline timestamp when playback state changes
            if (wasPlaying != MediaInfo.IsPlaying)
            {
                _lastTimelineUpdate = DateTime.MinValue;
                UpdateTimeline();
            }

            MediaInfo.IsShuffle = info.IsShuffleActive ?? false;

            MediaInfo.RepeatMode = info.AutoRepeatMode switch
            {
                Windows.Media.MediaPlaybackAutoRepeatMode.List => 1,
                Windows.Media.MediaPlaybackAutoRepeatMode.Track => 2,
                _ => 0
            };
        }
        catch { }
    }

    private void UpdateTimeline()
    {
        if (_session == null) return;

        try
        {
            var timeline = _session.GetTimelineProperties();
            if (timeline == null) return;

            _basePosition = timeline.Position;
            MediaInfo.Position = _basePosition;
            MediaInfo.Duration = timeline.EndTime;
            _lastTimelineUpdate = DateTime.UtcNow;
        }
        catch { }
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (_session == null || !MediaInfo.IsPlaying) return;

        // Interpolate position locally for smooth updates between timeline queries
        if (_lastTimelineUpdate != DateTime.MinValue && MediaInfo.Duration.TotalSeconds > 0)
        {
            double elapsed = (DateTime.UtcNow - _lastTimelineUpdate).TotalSeconds;
            var interpolatedPosition = _basePosition + TimeSpan.FromSeconds(elapsed);

            // Clamp to duration to prevent overflow
            MediaInfo.Position = interpolatedPosition <= MediaInfo.Duration
                ? interpolatedPosition
                : MediaInfo.Duration;
        }

        // Query actual timeline every second to resync and prevent drift
        if ((DateTime.UtcNow - _lastTimelineUpdate).TotalSeconds >= 1.0)
        {
            UpdateTimeline();
        }
    }

    // Transport controls
    public async Task PlayPauseAsync()
    {
        if (_session == null) return;
        try { await _session.TryTogglePlayPauseAsync(); } catch { }
    }

    public async Task NextAsync()
    {
        if (_session == null) return;
        try { await _session.TrySkipNextAsync(); } catch { }
    }

    public async Task PreviousAsync()
    {
        if (_session == null) return;
        try { await _session.TrySkipPreviousAsync(); } catch { }
    }

    public async Task ToggleShuffleAsync()
    {
        if (_session == null) return;
        try { await _session.TryChangeShuffleActiveAsync(!MediaInfo.IsShuffle); } catch { }
    }

    public async Task CycleRepeatAsync()
    {
        if (_session == null) return;
        try
        {
            var next = MediaInfo.RepeatMode switch
            {
                0 => Windows.Media.MediaPlaybackAutoRepeatMode.List,
                1 => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
                _ => Windows.Media.MediaPlaybackAutoRepeatMode.None
            };
            await _session.TryChangeAutoRepeatModeAsync(next);
        }
        catch { }
    }

    public async Task SeekToAsync(double percent)
    {
        if (_session == null || MediaInfo.Duration.TotalSeconds <= 0) return;
        try
        {
            var targetTicks = (long)(percent * MediaInfo.Duration.Ticks);
            await _session.TryChangePlaybackPositionAsync(targetTicks);

            // Reset timeline timestamp to force immediate update after seek
            _lastTimelineUpdate = DateTime.MinValue;
        }
        catch { }
    }

    public void Dispose()
    {
        _positionTimer.Stop();
        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        if (_manager != null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager.SessionsChanged -= OnSessionsChanged;
        }
    }
}
