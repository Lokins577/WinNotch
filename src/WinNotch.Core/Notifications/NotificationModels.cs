namespace WinNotch.Notifications;

public enum NotificationAccessState
{
    Stopped,
    Unsupported,
    NotRequested,
    Denied,
    Allowed,
    Error
}

public sealed record NotificationActivity(
    uint Id,
    string AppName,
    string Title,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record NotificationInboxSnapshot(
    NotificationAccessState Access,
    IReadOnlyList<NotificationActivity> Notifications,
    string? Message = null);
