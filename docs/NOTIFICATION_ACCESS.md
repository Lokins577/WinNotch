# Notification access

WinNotch uses the supported Windows `UserNotificationListener` API. It never
reads the notification database directly and does not scrape notification UI.

Windows requires both:

1. an MSIX package identity whose `Package.appxmanifest` declares the User
   Notification Listener capability; and
2. explicit permission from the signed-in user.

Microsoft documents those requirements in
[Notification listener: Access all notifications](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/notification-listener).

The signed MSIX release declares `uap3:userNotificationListener` and
`runFullTrust`, so it can request user consent and use the implemented inbox.
The Inno/standalone build remains available but has no package identity and
therefore reports `Requires package identity`. The listener, consent flow,
inbox synchronization, foreground change events, per-notification dismissal,
and revocation handling are shared by both distributions.

Denied access is not retried silently. The feature shows the denied state and
leaves the rest of WinNotch operational. Access is checked again before every
inbox refresh because Windows allows the user to revoke it at any time.
