# Notification access

WinNotch uses the supported Windows `UserNotificationListener` API. It never
reads the notification database directly and does not scrape notification UI.

Windows requires both:

1. an MSIX package identity whose `Package.appxmanifest` declares the User
   Notification Listener capability; and
2. explicit permission from the signed-in user.

Microsoft documents those requirements in
[Notification listener: Access all notifications](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/notification-listener).

The current Inno Setup build has no package identity, so the feature reports
`Unsupported` and explains that the packaged build is required. The listener,
consent flow, inbox synchronization, foreground change events, per-notification
dismissal, and revocation handling are already implemented. Product hardening
module 15 owns MSIX identity, signing, migration, and distribution.

Denied access is not retried silently. The feature shows the denied state and
leaves the rest of WinNotch operational. Access is checked again before every
inbox refresh because Windows allows the user to revoke it at any time.
