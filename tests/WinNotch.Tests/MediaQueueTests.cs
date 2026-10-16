using System.Net;
using System.Net.Http;
using System.Text;
using WinNotch.Music;
using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Settings;
using WinNotch.Views;
using Xunit;

namespace WinNotch.Tests;

public sealed class MediaQueueTests
{
    [Fact]
    public async Task SpotifyAdapter_SendsBearerTokenAndParsesTracksAndEpisodes()
    {
        HttpRequestMessage? captured = null;
        const string json = """
            {
              "currently_playing":{"name":"Current","artists":[{"name":"Artist A"}]},
              "queue":[
                {"name":"Next track","artists":[{"name":"Artist B"},{"name":"Artist C"}]},
                {"name":"Next episode","show":{"name":"Podcast"}}
              ]
            }
            """;
        using var client = new HttpClient(new StubHandler(request =>
        {
            captured = request;
            return JsonResponse(json);
        }));
        var adapter = new SpotifyMediaQueueAdapter(() => true, () => "secret-token", client);

        var items = await adapter.GetQueueAsync(CancellationToken.None);

        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("secret-token", captured.Headers.Authorization.Parameter);
        Assert.Equal("https://api.spotify.com/v1/me/player/queue", captured.RequestUri!.AbsoluteUri);
        Assert.Collection(items,
            current =>
            {
                Assert.True(current.IsCurrent);
                Assert.Equal("Current", current.Title);
                Assert.Equal("Artist A", current.Subtitle);
            },
            next => Assert.Equal("Artist B, Artist C", next.Subtitle),
            episode => Assert.Equal("Podcast", episode.Subtitle));
    }

    [Fact]
    public async Task SpotifyAdapter_MapsRejectedTokenToAuthenticationState()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var adapter = new SpotifyMediaQueueAdapter(() => true, () => "expired", client);

        await Assert.ThrowsAsync<MediaQueueAuthenticationException>(
            () => adapter.GetQueueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task VlcAdapter_UsesLoopbackPlaylistEndpointAndParsesTree()
    {
        HttpRequestMessage? captured = null;
        const string json = """
            {"children":[{"name":"Playlist","type":"node","children":[
              {"name":"Playing","type":"leaf","uri":"file:///a.mp3","current":"current"},
              {"name":"Later","type":"leaf","uri":"file:///b.mp3"}
            ]}]}
            """;
        using var client = new HttpClient(new StubHandler(request =>
        {
            captured = request;
            return JsonResponse(json);
        }));
        var adapter = new VlcMediaQueueAdapter(
            () => true,
            () => "http://127.0.0.1:8080/",
            () => "vlc-pass",
            client);

        var items = await adapter.GetQueueAsync(CancellationToken.None);

        Assert.Equal("http://127.0.0.1:8080/requests/playlist.json", captured!.RequestUri!.AbsoluteUri);
        Assert.Equal("Basic", captured.Headers.Authorization!.Scheme);
        Assert.Equal(":vlc-pass", Encoding.UTF8.GetString(
            Convert.FromBase64String(captured.Headers.Authorization.Parameter!)));
        Assert.Equal(2, items.Count);
        Assert.True(items[0].IsCurrent);
        Assert.Equal("Later", items[1].Title);
    }

    [Theory]
    [InlineData("https://example.com:8080/")]
    [InlineData("file:///C:/vlc/")]
    [InlineData("http://user:pass@127.0.0.1:8080/")]
    [InlineData("not-a-url")]
    public void VlcPreferences_RejectNonLoopbackOrEmbeddedCredentials(string value)
    {
        Assert.False(MediaQueuePreferences.TryGetLoopbackBaseUri(value, out _));
    }

    [Fact]
    public void QueuePreferences_NormalizeRefreshAndLoopbackUrl()
    {
        var preferences = new MediaQueuePreferences
        {
            RefreshSeconds = 1,
            VlcBaseUrl = "http://localhost:8080"
        };

        preferences.Normalize();

        Assert.Equal(5, preferences.RefreshSeconds);
        Assert.Equal("http://localhost:8080/", preferences.VlcBaseUrl);
    }

    [Fact]
    public async Task QueueService_SelectsAdapterAndPublishesBoundedSnapshot()
    {
        using var media = new MediaService();
        media.MediaInfo.SourceId = "SpotifyAB.SpotifyMusic_test!Spotify.exe";
        var adapter = new FakeAdapter(
            Enumerable.Range(1, 120)
                .Select(number => new MediaQueueItem($"Track {number}"))
                .ToArray());
        var preferences = new MediaQueuePreferences { SpotifyEnabled = true };
        using var service = new MediaQueueService(media, [adapter], () => preferences);

        await service.RefreshAsync();

        Assert.Equal(MediaQueueCapability.Available, service.Snapshot.Capability);
        Assert.Equal("Spotify", service.Snapshot.Provider);
        Assert.Equal(100, service.Snapshot.Queue.Count);
        Assert.Equal(1, adapter.CallCount);
    }

    [Fact]
    public void QueueWindows_BuildOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                var credentials = new MemoryCredentialStore();
                using var media = new MediaService();
                using var queue = new MediaQueueService(media, [], () => settings.MediaQueuePreferences);
                var settingsWindow = new MediaQueueSettingsWindow(
                    settings, new MemorySettingsStore(), credentials, queue, () => { });
                var queueWindow = new MediaQueueWindow(queue);
                Assert.NotNull(settingsWindow.Content);
                Assert.NotNull(queueWindow.Content);
                settingsWindow.Close();
                queueWindow.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Media queue UI test timed out.");
        Assert.Null(failure);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private sealed class FakeAdapter(IReadOnlyList<MediaQueueItem> items) : IMediaQueueAdapter
    {
        public int CallCount { get; private set; }
        public string SourceId => "spotify";
        public string DisplayName => "Spotify";
        public bool IsEnabled => true;
        public bool IsConfigured => true;
        public bool CanHandle(string mediaSourceId) =>
            mediaSourceId.Contains(SourceId, StringComparison.OrdinalIgnoreCase);

        public Task<IReadOnlyList<MediaQueueItem>> GetQueueAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(items);
        }
    }

    private sealed class MemorySettingsStore : IAppSettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
        public void ApplyStartOnBoot(bool enabled) { }
    }

    private sealed class MemoryCredentialStore : IMediaCredentialStore
    {
        private readonly Dictionary<string, string> _values = new();
        public string? Read(string key) => _values.GetValueOrDefault(key);
        public void Write(string key, string secret) => _values[key] = secret;
        public void Delete(string key) => _values.Remove(key);
    }
}
