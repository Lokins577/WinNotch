namespace WinNotch.Music;

public sealed record MediaSourceInfo(string Id, string DisplayName, bool IsCurrent);

public enum MediaQueueCapability
{
    Unsupported,
    Available
}

public interface IMediaQueueAdapter
{
    string SourceId { get; }
    Task<IReadOnlyList<string>> GetQueueAsync(CancellationToken cancellationToken);
}
