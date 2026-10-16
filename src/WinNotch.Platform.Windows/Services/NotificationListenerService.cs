using Windows.ApplicationModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using WinNotch.Features;
using WinNotch.Notifications;

namespace WinNotch.Services;

public interface INotificationListenerBackend : IDisposable
{
    bool IsSupported { get; }
    NotificationAccessState GetAccessState();
    Task<NotificationAccessState> RequestAccessAsync();
    Task<IReadOnlyList<NotificationActivity>> GetNotificationsAsync();
    void Remove(uint id);
    event Action? Changed;
}

public sealed class NotificationListenerService : IFeatureModule, IDisposable
{
    private readonly INotificationListenerBackend _backend;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _started;
    private NotificationInboxSnapshot _snapshot = new(NotificationAccessState.Stopped, []);

    public NotificationListenerService(INotificationListenerBackend? backend = null)
    {
        _backend = backend ?? new WindowsNotificationListenerBackend();
    }

    public FeatureId Id => BuiltInFeatures.Notifications.Id;
    public NotificationInboxSnapshot Snapshot => _snapshot;
    public event Action<NotificationInboxSnapshot>? Updated;

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        if (_started) return;
        _started = true;
        _backend.Changed += OnBackendChanged;

        if (!_backend.IsSupported)
        {
            Publish(NotificationAccessState.Unsupported, [],
                "Notification mirroring requires an MSIX package identity and the userNotificationListener capability.");
            return;
        }

        var access = _backend.GetAccessState();
        if (access == NotificationAccessState.Allowed)
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        else
            Publish(access, [], AccessMessage(access));
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (!_started) return ValueTask.CompletedTask;
        _backend.Changed -= OnBackendChanged;
        _started = false;
        Publish(NotificationAccessState.Stopped, []);
        return ValueTask.CompletedTask;
    }

    public async Task RequestAccessAsync(CancellationToken cancellationToken = default)
    {
        if (!_backend.IsSupported)
        {
            Publish(NotificationAccessState.Unsupported, [],
                "Install the packaged build before requesting notification access.");
            return;
        }

        var access = await _backend.RequestAccessAsync().ConfigureAwait(true);
        if (access == NotificationAccessState.Allowed)
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        else
            Publish(access, [], AccessMessage(access));
    }

    public async Task DismissAsync(uint id, CancellationToken cancellationToken = default)
    {
        if (_snapshot.Access != NotificationAccessState.Allowed) return;
        _backend.Remove(id);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var access = _backend.GetAccessState();
            if (access != NotificationAccessState.Allowed)
            {
                Publish(access, [], AccessMessage(access));
                return;
            }

            var notifications = await _backend.GetNotificationsAsync().ConfigureAwait(false);
            Publish(NotificationAccessState.Allowed,
                notifications.OrderByDescending(item => item.CreatedAt).Take(20).ToArray());
        }
        catch (Exception exception)
        {
            Publish(NotificationAccessState.Error, [], exception.Message);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async void OnBackendChanged()
    {
        try { await RefreshAsync().ConfigureAwait(false); }
        catch { }
    }

    private void Publish(
        NotificationAccessState access,
        IReadOnlyList<NotificationActivity> notifications,
        string? message = null)
    {
        _snapshot = new NotificationInboxSnapshot(access, notifications, message);
        Updated?.Invoke(_snapshot);
    }

    private static string? AccessMessage(NotificationAccessState access)
    {
        return access switch
        {
            NotificationAccessState.NotRequested => "Allow notification access to show the inbox in WinNotch.",
            NotificationAccessState.Denied => "Notification access is denied. Change it in Windows Settings.",
            NotificationAccessState.Unsupported => "Notification listener is unavailable in this build.",
            _ => null
        };
    }

    public void Dispose()
    {
        StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
        _backend.Dispose();
        _refreshGate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class WindowsNotificationListenerBackend : INotificationListenerBackend
{
    private readonly UserNotificationListener? _listener;

    public WindowsNotificationListenerBackend()
    {
        IsSupported = HasPackageIdentity();
        if (!IsSupported) return;

        try
        {
            _listener = UserNotificationListener.Current;
            _listener.NotificationChanged += OnNotificationChanged;
        }
        catch
        {
            IsSupported = false;
        }
    }

    public bool IsSupported { get; private set; }
    public event Action? Changed;

    public NotificationAccessState GetAccessState()
    {
        if (!IsSupported || _listener == null) return NotificationAccessState.Unsupported;
        try
        {
            return Map(_listener.GetAccessStatus());
        }
        catch
        {
            return NotificationAccessState.Error;
        }
    }

    public async Task<NotificationAccessState> RequestAccessAsync()
    {
        if (!IsSupported || _listener == null) return NotificationAccessState.Unsupported;
        try
        {
            return Map(await _listener.RequestAccessAsync());
        }
        catch
        {
            return NotificationAccessState.Error;
        }
    }

    public async Task<IReadOnlyList<NotificationActivity>> GetNotificationsAsync()
    {
        if (!IsSupported || _listener == null) return [];

        var notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
        var result = new List<NotificationActivity>(notifications.Count);
        foreach (var notification in notifications)
        {
            try
            {
                var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                var text = binding?.GetTextElements().Select(item => item.Text).ToArray() ?? [];
                result.Add(new NotificationActivity(
                    notification.Id,
                    notification.AppInfo.DisplayInfo.DisplayName ?? "Application",
                    text.FirstOrDefault() ?? "Notification",
                    string.Join(Environment.NewLine, text.Skip(1)),
                    notification.CreationTime));
            }
            catch
            {
                // One malformed notification must not hide the rest of the inbox.
            }
        }
        return result;
    }

    public void Remove(uint id)
    {
        if (IsSupported && _listener != null)
            _listener.RemoveNotification(id);
    }

    private void OnNotificationChanged(
        UserNotificationListener sender,
        UserNotificationChangedEventArgs args)
    {
        Changed?.Invoke();
    }

    private static bool HasPackageIdentity()
    {
        try
        {
            _ = Package.Current.Id.Name;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static NotificationAccessState Map(UserNotificationListenerAccessStatus access)
    {
        return access switch
        {
            UserNotificationListenerAccessStatus.Allowed => NotificationAccessState.Allowed,
            UserNotificationListenerAccessStatus.Denied => NotificationAccessState.Denied,
            _ => NotificationAccessState.NotRequested
        };
    }

    public void Dispose()
    {
        if (_listener != null)
            _listener.NotificationChanged -= OnNotificationChanged;
    }
}
