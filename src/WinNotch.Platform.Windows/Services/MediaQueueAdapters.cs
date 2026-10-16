using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WinNotch.Music;

namespace WinNotch.Services;

public sealed class SpotifyMediaQueueAdapter : IMediaQueueAdapter
{
    private static readonly Uri QueueEndpoint = new("https://api.spotify.com/v1/me/player/queue");
    private readonly Func<bool> _enabled;
    private readonly Func<string?> _accessToken;
    private readonly HttpClient _httpClient;

    public SpotifyMediaQueueAdapter(
        Func<bool> enabled,
        Func<string?> accessToken,
        HttpClient? httpClient = null)
    {
        _enabled = enabled;
        _accessToken = accessToken;
        _httpClient = httpClient ?? CreateProviderClient();
    }

    public string SourceId => "spotify";
    public string DisplayName => "Spotify";
    public bool IsEnabled => _enabled();
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_accessToken());

    public bool CanHandle(string mediaSourceId) =>
        !string.IsNullOrWhiteSpace(mediaSourceId)
        && mediaSourceId.Contains(SourceId, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<MediaQueueItem>> GetQueueAsync(CancellationToken cancellationToken)
    {
        string? token = _accessToken();
        if (string.IsNullOrWhiteSpace(token))
            throw new MediaQueueAuthenticationException("Add a Spotify access token in Media queue settings.");

        using var request = new HttpRequestMessage(HttpMethod.Get, QueueEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new MediaQueueAuthenticationException(
                "Spotify rejected the token. Create a new token with user-read-playback-state and user-read-currently-playing.");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Spotify rate-limited the queue request. Try again later.", null, response.StatusCode);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new HttpRequestException("Spotify queue redirects are not accepted.", null, response.StatusCode);
        response.EnsureSuccessStatusCode();

        await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var result = new List<MediaQueueItem>();
        if (document.RootElement.TryGetProperty("currently_playing", out var current)
            && current.ValueKind == JsonValueKind.Object
            && TryParseItem(current, out var currentItem))
            result.Add(currentItem with { IsCurrent = true });

        if (document.RootElement.TryGetProperty("queue", out var queue)
            && queue.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in queue.EnumerateArray())
                if (TryParseItem(element, out var item)) result.Add(item);
        }
        return result;
    }

    private static bool TryParseItem(JsonElement element, out MediaQueueItem item)
    {
        item = null!;
        string? title = GetString(element, "name");
        if (string.IsNullOrWhiteSpace(title)) return false;

        string? subtitle = null;
        if (element.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
        {
            subtitle = string.Join(", ", artists.EnumerateArray()
                .Select(artist => GetString(artist, "name"))
                .Where(name => !string.IsNullOrWhiteSpace(name)));
        }
        else if (element.TryGetProperty("show", out var show) && show.ValueKind == JsonValueKind.Object)
        {
            subtitle = GetString(show, "name") ?? GetString(element, "publisher");
        }

        item = new MediaQueueItem(title.Trim(), string.IsNullOrWhiteSpace(subtitle) ? null : subtitle);
        return true;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static HttpClient CreateProviderClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
}

public sealed class VlcMediaQueueAdapter : IMediaQueueAdapter
{
    private readonly Func<bool> _enabled;
    private readonly Func<string?> _baseUrl;
    private readonly Func<string?> _password;
    private readonly HttpClient _httpClient;

    public VlcMediaQueueAdapter(
        Func<bool> enabled,
        Func<string?> baseUrl,
        Func<string?> password,
        HttpClient? httpClient = null)
    {
        _enabled = enabled;
        _baseUrl = baseUrl;
        _password = password;
        _httpClient = httpClient ?? CreateLoopbackClient();
    }

    public string SourceId => "vlc";
    public string DisplayName => "VLC";
    public bool IsEnabled => _enabled();
    public bool IsConfigured => MediaQueuePreferences.TryGetLoopbackBaseUri(_baseUrl(), out _)
                                && !string.IsNullOrEmpty(_password());

    public bool CanHandle(string mediaSourceId) =>
        !string.IsNullOrWhiteSpace(mediaSourceId)
        && mediaSourceId.Contains(SourceId, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<MediaQueueItem>> GetQueueAsync(CancellationToken cancellationToken)
    {
        if (!MediaQueuePreferences.TryGetLoopbackBaseUri(_baseUrl(), out var baseUri))
            throw new InvalidOperationException("VLC Web URL must be an HTTP(S) loopback address.");
        string? password = _password();
        if (string.IsNullOrEmpty(password))
            throw new MediaQueueAuthenticationException("Add the VLC Web interface password in Media queue settings.");

        var endpoint = new Uri(baseUri, "requests/playlist.json");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($":{password}")));
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new MediaQueueAuthenticationException("VLC rejected the Web interface password.");
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new HttpRequestException("VLC redirects are blocked so its password cannot leave the local machine.", null, response.StatusCode);
        response.EnsureSuccessStatusCode();

        await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var items = new List<MediaQueueItem>();
        CollectItems(document.RootElement, items);
        return items;
    }

    private static void CollectItems(JsonElement element, List<MediaQueueItem> items)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) CollectItems(child, items);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) return;

        string? type = GetString(element, "type");
        string? name = GetString(element, "name");
        bool isLeaf = string.Equals(type, "leaf", StringComparison.OrdinalIgnoreCase)
                      || element.TryGetProperty("uri", out _);
        if (isLeaf && !string.IsNullOrWhiteSpace(name))
        {
            bool current = element.TryGetProperty("current", out var currentValue)
                           && currentValue.ValueKind == JsonValueKind.String
                           && string.Equals(currentValue.GetString(), "current", StringComparison.OrdinalIgnoreCase);
            items.Add(new MediaQueueItem(name.Trim(), IsCurrent: current));
        }

        if (element.TryGetProperty("children", out var children)) CollectItems(children, items);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static HttpClient CreateLoopbackClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };
}
