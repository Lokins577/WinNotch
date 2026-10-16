using System.Windows.Threading;
using System.Net.Http;
using WinNotch.Music;

namespace WinNotch.Services;

/// <summary>
/// Chooses a provider adapter for the active GSMTC source and publishes a
/// bounded, refreshable queue snapshot. It never guesses a queue for sources
/// that do not expose one through a configured provider API.
/// </summary>
public sealed class MediaQueueService : IDisposable
{
    private readonly MediaService _mediaService;
    private readonly IReadOnlyList<IMediaQueueAdapter> _adapters;
    private readonly Func<MediaQueuePreferences> _preferences;
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private volatile bool _disposed;
    private MediaQueueSnapshot _snapshot = new(MediaQueueCapability.Unsupported,
        message: "The active media source has no configured queue adapter.");

    public MediaQueueService(
        MediaService mediaService,
        IEnumerable<IMediaQueueAdapter> adapters,
        Func<MediaQueuePreferences> preferences)
    {
        _mediaService = mediaService;
        _adapters = adapters.ToArray();
        _preferences = preferences;
        _timer = new DispatcherTimer { Interval = GetRefreshInterval() };
        _timer.Tick += OnTimerTick;
        _mediaService.SessionChanged += OnSessionChanged;
        _timer.Start();
    }

    public MediaQueueSnapshot Snapshot => _snapshot;
    public event Action<MediaQueueSnapshot>? Updated;

    public void Reconfigure()
    {
        if (_disposed) return;
        _preferences().Normalize();
        _timer.Interval = GetRefreshInterval();
        _ = RefreshAsync();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        bool entered;
        try
        {
            entered = await _refreshGate.WaitAsync(0, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        if (!entered) return;
        try
        {
            string sourceId = _mediaService.MediaInfo.SourceId;
            var adapter = _adapters.FirstOrDefault(candidate => candidate.CanHandle(sourceId));
            if (adapter == null)
            {
                Publish(new MediaQueueSnapshot(MediaQueueCapability.Unsupported,
                    message: "The active media source does not expose a supported queue API."));
                return;
            }
            bool enabled;
            try
            {
                enabled = adapter.IsEnabled;
            }
            catch (Exception exception)
            {
                Publish(new MediaQueueSnapshot(MediaQueueCapability.Failed,
                    adapter.DisplayName, message: exception.Message));
                return;
            }
            if (!enabled)
            {
                Publish(new MediaQueueSnapshot(MediaQueueCapability.Disabled, adapter.DisplayName,
                    message: $"Enable the {adapter.DisplayName} queue adapter in Settings."));
                return;
            }
            bool configured;
            try
            {
                configured = adapter.IsConfigured;
            }
            catch (Exception exception)
            {
                Publish(new MediaQueueSnapshot(MediaQueueCapability.Failed,
                    adapter.DisplayName, message: exception.Message));
                return;
            }
            if (!configured)
            {
                Publish(new MediaQueueSnapshot(MediaQueueCapability.NotConfigured, adapter.DisplayName,
                    message: $"Finish {adapter.DisplayName} queue setup in Settings."));
                return;
            }

            Publish(new MediaQueueSnapshot(MediaQueueCapability.Loading, adapter.DisplayName,
                _snapshot.Provider == adapter.DisplayName ? _snapshot.Queue : [], "Refreshing queue…"));
            try
            {
                var items = await adapter.GetQueueAsync(linked.Token).ConfigureAwait(false);
                Publish(new MediaQueueSnapshot(MediaQueueCapability.Available, adapter.DisplayName,
                    items.Take(100).ToArray(),
                    items.Count == 0 ? "The provider queue is empty." : null,
                    DateTimeOffset.Now));
            }
            catch (MediaQueueAuthenticationException exception)
            {
                Publish(new MediaQueueSnapshot(MediaQueueCapability.AuthenticationRequired,
                    adapter.DisplayName, message: exception.Message));
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception)
            {
                Publish(new MediaQueueSnapshot(MediaQueueCapability.Unavailable,
                    adapter.DisplayName, message: exception.Message));
            }
            catch (Exception exception)
            {
                Publish(new MediaQueueSnapshot(MediaQueueCapability.Failed,
                    adapter.DisplayName, message: exception.Message));
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void OnSessionChanged() => _ = RefreshAsync();
    private void OnTimerTick(object? sender, EventArgs args) => _ = RefreshAsync();

    private TimeSpan GetRefreshInterval() =>
        TimeSpan.FromSeconds(Math.Clamp(_preferences().RefreshSeconds, 5, 300));

    private void Publish(MediaQueueSnapshot snapshot)
    {
        if (_disposed) return;
        _snapshot = snapshot;
        Updated?.Invoke(snapshot);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _mediaService.SessionChanged -= OnSessionChanged;
        _lifetime.Cancel();
        // A provider request may still be unwinding. Leaving the two tiny
        // synchronization handles undisposed avoids a Dispose/finally race.
    }
}
