using WinNotch.Notifications;
using WinNotch.Services;
using Xunit;

namespace WinNotch.Tests;

public sealed class NotificationListenerServiceTests
{
    [Fact]
    public async Task UnsupportedBackend_ReportsExplicitFallback()
    {
        var backend = new FakeBackend { IsSupported = false };
        await using var service = new NotificationListenerService(backend);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(NotificationAccessState.Unsupported, service.Snapshot.Access);
        Assert.Contains("MSIX", service.Snapshot.Message);
    }

    [Fact]
    public async Task AllowedBackend_ListsAndDismissesNotifications()
    {
        var backend = new FakeBackend
        {
            Access = NotificationAccessState.Allowed,
            Notifications =
            [
                new NotificationActivity(7, "Mail", "Hello", "Message", DateTimeOffset.UtcNow)
            ]
        };
        await using var service = new NotificationListenerService(backend);

        await service.StartAsync(CancellationToken.None);
        Assert.Single(service.Snapshot.Notifications);

        await service.DismissAsync(7);
        Assert.Equal((uint)7, backend.RemovedId);
        Assert.Empty(service.Snapshot.Notifications);
    }

    [Fact]
    public async Task RequestAccess_RefreshesInboxAfterConsent()
    {
        var backend = new FakeBackend
        {
            Access = NotificationAccessState.NotRequested,
            RequestedAccess = NotificationAccessState.Allowed,
            Notifications =
            [
                new NotificationActivity(1, "Calendar", "Meeting", "Now", DateTimeOffset.UtcNow)
            ]
        };
        await using var service = new NotificationListenerService(backend);
        await service.StartAsync(CancellationToken.None);

        await service.RequestAccessAsync();

        Assert.Equal(NotificationAccessState.Allowed, service.Snapshot.Access);
        Assert.Single(service.Snapshot.Notifications);
    }

    private sealed class FakeBackend : INotificationListenerBackend
    {
        public bool IsSupported { get; set; } = true;
        public NotificationAccessState Access { get; set; } = NotificationAccessState.NotRequested;
        public NotificationAccessState RequestedAccess { get; set; } = NotificationAccessState.Denied;
        public IReadOnlyList<NotificationActivity> Notifications { get; set; } = [];
        public uint? RemovedId { get; private set; }
        public event Action? Changed;

        public NotificationAccessState GetAccessState() => Access;

        public Task<NotificationAccessState> RequestAccessAsync()
        {
            Access = RequestedAccess;
            return Task.FromResult(Access);
        }

        public Task<IReadOnlyList<NotificationActivity>> GetNotificationsAsync()
        {
            return Task.FromResult(Notifications);
        }

        public void Remove(uint id)
        {
            RemovedId = id;
            Notifications = Notifications.Where(item => item.Id != id).ToArray();
            Changed?.Invoke();
        }

        public void Dispose() { }
    }
}
