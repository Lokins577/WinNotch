namespace WinNotch.Music;

public sealed record MediaSourceInfo(string Id, string DisplayName, bool IsCurrent);

public enum MediaQueueCapability
{
    Unsupported,
    Disabled,
    NotConfigured,
    Loading,
    Available,
    AuthenticationRequired,
    Unavailable,
    Failed
}

public sealed record MediaQueueItem(
    string Title,
    string? Subtitle = null,
    bool IsCurrent = false);

public sealed record MediaQueueSnapshot
{
    public MediaQueueSnapshot(
        MediaQueueCapability capability,
        string? provider = null,
        IReadOnlyList<MediaQueueItem>? queue = null,
        string? message = null,
        DateTimeOffset? updatedAt = null)
    {
        Capability = capability;
        Provider = provider;
        Queue = queue ?? [];
        Message = message;
        UpdatedAt = updatedAt;
    }

    public MediaQueueCapability Capability { get; init; }
    public string? Provider { get; init; }
    public IReadOnlyList<MediaQueueItem> Queue { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed class MediaQueueAuthenticationException : Exception
{
    public MediaQueueAuthenticationException(string message) : base(message) { }
}

public interface IMediaQueueAdapter
{
    string SourceId { get; }
    string DisplayName { get; }
    bool IsEnabled { get; }
    bool IsConfigured { get; }
    bool CanHandle(string mediaSourceId);
    Task<IReadOnlyList<MediaQueueItem>> GetQueueAsync(CancellationToken cancellationToken);
}

public interface IMediaCredentialStore
{
    string? Read(string key);
    void Write(string key, string secret);
    void Delete(string key);
}

public static class MediaCredentialKeys
{
    public const string SpotifyAccessToken = "SpotifyAccessToken";
    public const string VlcPassword = "VlcPassword";
}

public sealed class MediaQueuePreferences
{
    public bool SpotifyEnabled { get; set; }
    public bool VlcEnabled { get; set; }
    public string VlcBaseUrl { get; set; } = "http://127.0.0.1:8080/";
    public int RefreshSeconds { get; set; } = 15;

    public void Normalize()
    {
        RefreshSeconds = Math.Clamp(RefreshSeconds, 5, 300);
        if (!TryGetLoopbackBaseUri(VlcBaseUrl, out var uri))
            VlcBaseUrl = "http://127.0.0.1:8080/";
        else
            VlcBaseUrl = uri.AbsoluteUri;
    }

    public static bool TryGetLoopbackBaseUri(string? value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            || !candidate.IsLoopback
            || (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(candidate.UserInfo))
            return false;

        var builder = new UriBuilder(candidate)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };
        if (!builder.Path.EndsWith('/')) builder.Path += "/";
        uri = builder.Uri;
        return true;
    }
}
