using System.Net;
using System.Net.Http;
using System.Text;
using WinNotch.Music;
using WinNotch.Services;
using Xunit;

namespace WinNotch.Tests;

public sealed class LyricsServiceTests
{
    [Fact]
    public void LrcParser_ParsesAndOrdersTimestampedLines()
    {
        var result = LrcParser.Parse(
            "[00:10.50]Second\n[00:01.00][00:02.00]First",
            plainLyrics: null);

        Assert.True(result.IsSynced);
        Assert.Equal(3, result.Lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), result.Lines[0].Timestamp);
        Assert.Equal("First", result.Lines[0].Text);
    }

    [Fact]
    public void LrcParser_FallsBackToPlainLyrics()
    {
        var result = LrcParser.Parse(null, "Line one\n\nLine two");

        Assert.False(result.IsSynced);
        Assert.Equal(["Line one", "Line two"], result.Lines.Select(line => line.Text));
    }

    [Fact]
    public async Task ConsentGate_PreventsLookupUntilExplicitlyEnabled()
    {
        bool consent = false;
        var provider = new FakeLyricsProvider();
        using var media = new MediaService();
        using var lyrics = new LyricsService(media, provider, () => consent);
        media.MediaInfo.HasMedia = true;
        media.MediaInfo.Title = "Track";
        media.MediaInfo.Artist = "Artist";

        await Task.Delay(450);
        Assert.Equal(0, provider.CallCount);
        Assert.Equal(LyricsStatus.Disabled, lyrics.Snapshot.Status);

        consent = true;
        var available = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lyrics.Updated += snapshot =>
        {
            if (snapshot.Status == LyricsStatus.Available) available.TrySetResult();
        };
        lyrics.RefreshConsent();
        await available.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(LyricsStatus.Available, lyrics.Snapshot.Status);
    }

    [Fact]
    public async Task LrclibProvider_UsesEncodedMetadataAndParsesResponse()
    {
        var handler = new RecordingHandler("""
            {"instrumental":false,"plainLyrics":"Plain","syncedLyrics":"[00:01.25]Synced"}
            """);
        var client = new HttpClient(handler);
        var provider = new LrclibLyricsProvider("WinNotch-Test/1.0", client);

        var result = await provider.GetLyricsAsync(
            new LyricsTrack("A & B", "Artist", "Album", TimeSpan.FromSeconds(123)),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.IsSynced);
        Assert.Contains("track_name=A%20%26%20B", handler.RequestUri!.Query);
        Assert.Contains("WinNotch-Test/1.0", handler.ClientHeader);
    }

    private sealed class FakeLyricsProvider : ILyricsProvider
    {
        public int CallCount { get; private set; }
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LyricsResult?> GetLyricsAsync(LyricsTrack track, CancellationToken cancellationToken)
        {
            CallCount++;
            Called.TrySetResult();
            return Task.FromResult<LyricsResult?>(new LyricsResult([new LyricsLine(null, "Lyrics")], false));
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _json;
        public RecordingHandler(string json) => _json = json;
        public Uri? RequestUri { get; private set; }
        public string ClientHeader { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ClientHeader = string.Join(",", request.Headers.GetValues("Lrclib-Client"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
        }
    }
}
