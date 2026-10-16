using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using WinNotch.Models;
using WinNotch.Music;

namespace WinNotch.Services;

public interface ILyricsProvider
{
    Task<LyricsResult?> GetLyricsAsync(LyricsTrack track, CancellationToken cancellationToken);
}

public sealed class LyricsService : IDisposable
{
    private readonly MediaService _mediaService;
    private readonly ILyricsProvider _provider;
    private readonly Func<bool> _consentProvider;
    private readonly Dictionary<string, LyricsResult?> _cache = new(StringComparer.Ordinal);
    private CancellationTokenSource? _lookupCancellation;
    private LyricsSnapshot _snapshot;

    public LyricsService(
        MediaService mediaService,
        ILyricsProvider provider,
        Func<bool> consentProvider)
    {
        _mediaService = mediaService;
        _provider = provider;
        _consentProvider = consentProvider;
        _snapshot = consentProvider()
            ? new LyricsSnapshot(LyricsStatus.Idle)
            : new LyricsSnapshot(LyricsStatus.Disabled, Message: "Enable online lyrics in Settings.");
        _mediaService.MediaInfo.PropertyChanged += OnMediaPropertyChanged;
        _mediaService.SessionChanged += OnSessionChanged;
    }

    public LyricsSnapshot Snapshot => _snapshot;
    public event Action<LyricsSnapshot>? Updated;

    public void RefreshConsent()
    {
        _lookupCancellation?.Cancel();
        if (!_consentProvider())
        {
            Publish(new LyricsSnapshot(LyricsStatus.Disabled,
                Message: "Enable online lyrics in Settings."));
            return;
        }
        _ = QueueLookupAsync();
    }

    public LyricsLine? GetCurrentLine(TimeSpan position)
    {
        var result = _snapshot.Result;
        if (result == null || result.Lines.Count == 0) return null;
        if (!result.IsSynced) return result.Lines.FirstOrDefault();
        return result.Lines
            .Where(line => line.Timestamp <= position)
            .LastOrDefault()
            ?? result.Lines.FirstOrDefault();
    }

    private void OnSessionChanged() => _ = QueueLookupAsync();

    private void OnMediaPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MediaInfo.Title) or nameof(MediaInfo.Artist)
            or nameof(MediaInfo.AlbumTitle) or nameof(MediaInfo.Duration))
            _ = QueueLookupAsync();
    }

    private async Task QueueLookupAsync()
    {
        _lookupCancellation?.Cancel();
        _lookupCancellation?.Dispose();
        _lookupCancellation = new CancellationTokenSource();
        var cancellationToken = _lookupCancellation.Token;
        try
        {
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            await LookupAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private async Task LookupAsync(CancellationToken cancellationToken)
    {
        if (!_consentProvider())
        {
            Publish(new LyricsSnapshot(LyricsStatus.Disabled,
                Message: "Enable online lyrics in Settings."));
            return;
        }

        var media = _mediaService.MediaInfo;
        if (!media.HasMedia || string.IsNullOrWhiteSpace(media.Title)
            || string.IsNullOrWhiteSpace(media.Artist))
        {
            Publish(new LyricsSnapshot(LyricsStatus.Idle));
            return;
        }

        var track = new LyricsTrack(media.Title, media.Artist, media.AlbumTitle, media.Duration);
        string key = $"{track.Artist}\n{track.Title}\n{track.Album}\n{(int)track.Duration.TotalSeconds}";
        if (_cache.TryGetValue(key, out var cached))
        {
            Publish(cached == null
                ? new LyricsSnapshot(LyricsStatus.NotFound, Message: "Lyrics not found.")
                : new LyricsSnapshot(LyricsStatus.Available, cached));
            return;
        }

        Publish(new LyricsSnapshot(LyricsStatus.Loading));
        try
        {
            var result = await _provider.GetLyricsAsync(track, cancellationToken).ConfigureAwait(false);
            if (_cache.Count >= 64) _cache.Remove(_cache.Keys.First());
            _cache[key] = result;
            Publish(result == null
                ? new LyricsSnapshot(LyricsStatus.NotFound, Message: "Lyrics not found.")
                : new LyricsSnapshot(LyricsStatus.Available, result));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            Publish(new LyricsSnapshot(LyricsStatus.Failed, Message: exception.Message));
        }
    }

    private void Publish(LyricsSnapshot snapshot)
    {
        _snapshot = snapshot;
        Updated?.Invoke(snapshot);
    }

    public void Dispose()
    {
        _lookupCancellation?.Cancel();
        _lookupCancellation?.Dispose();
        _mediaService.MediaInfo.PropertyChanged -= OnMediaPropertyChanged;
        _mediaService.SessionChanged -= OnSessionChanged;
    }
}

public sealed class LrclibLyricsProvider : ILyricsProvider
{
    private readonly HttpClient _httpClient;

    public LrclibLyricsProvider(string clientIdentity, HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Lrclib-Client", clientIdentity);
    }

    public async Task<LyricsResult?> GetLyricsAsync(
        LyricsTrack track,
        CancellationToken cancellationToken)
    {
        string url = "https://lrclib.net/api/get"
            + $"?artist_name={Uri.EscapeDataString(track.Artist)}"
            + $"&track_name={Uri.EscapeDataString(track.Title)}"
            + $"&album_name={Uri.EscapeDataString(track.Album ?? string.Empty)}"
            + $"&duration={(int)Math.Round(track.Duration.TotalSeconds)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LrclibResponse>(
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (payload == null) return null;
        return LrcParser.Parse(payload.SyncedLyrics, payload.PlainLyrics, payload.Instrumental);
    }

    private sealed class LrclibResponse
    {
        [JsonPropertyName("instrumental")]
        public bool Instrumental { get; set; }

        [JsonPropertyName("plainLyrics")]
        public string? PlainLyrics { get; set; }

        [JsonPropertyName("syncedLyrics")]
        public string? SyncedLyrics { get; set; }
    }
}
