using System.Diagnostics;
using Windows.Devices.Radios;
using Windows.UI.Notifications.Management;
using WinNotch.ProductHardening;

namespace WinNotch.Platform.Windows.ProductHardening;

public sealed class PermissionHubService
{
    public async Task<IReadOnlyList<PermissionCapability>> GetCapabilitiesAsync()
    {
        bool identity = PackageIdentityService.HasIdentity;
        var result = new List<PermissionCapability>
        {
            new("package", "MSIX package identity",
                identity ? PermissionStatus.Available : PermissionStatus.Unsupported,
                identity ? $"Package: {PackageIdentityService.PackageName}" : "Install the signed MSIX build for identity-gated Windows APIs."),
            new("camera", "Camera privacy", PermissionStatus.Available,
                "Controlled by Windows privacy settings and requested only when camera preview starts.", "ms-settings:privacy-webcam"),
            new("microphone", "Microphone privacy", PermissionStatus.Available,
                "Controlled by Windows privacy settings and used only for explicit recording.", "ms-settings:privacy-microphone"),
            new("input", "Global input hooks", PermissionStatus.Available,
                "Controlled by the opt-in Input enhancements feature; no elevation or driver is used."),
            new("capture", "Screen capture", PermissionStatus.Available,
                "Region/display capture is explicit and local; protected content is not bypassed."),
            new("startup", "Startup application", PermissionStatus.Available,
                identity ? "Managed by the packaged startup task." : "Managed by the current-user Run registry value.", "ms-settings:startupapps")
        };
        result.Add(ReadNotificationCapability(identity));
        try
        {
            var radios = await Radio.GetRadiosAsync();
            result.Add(new PermissionCapability(
                "radios", "Bluetooth radio control",
                radios.Any(value => value.Kind == RadioKind.Bluetooth) ? PermissionStatus.NotRequested : PermissionStatus.Unsupported,
                radios.Any(value => value.Kind == RadioKind.Bluetooth)
                    ? "Permission is requested only when a Bluetooth sleep policy is enabled."
                    : "No Bluetooth radio is exposed.",
                "ms-settings:bluetooth"));
        }
        catch (Exception exception)
        {
            result.Add(new PermissionCapability("radios", "Bluetooth radio control", PermissionStatus.Unsupported, exception.Message));
        }
        return result;
    }

    public async Task<PermissionStatus> RequestNotificationAccessAsync()
    {
        if (!PackageIdentityService.HasIdentity) return PermissionStatus.RequiresPackageIdentity;
        var status = await UserNotificationListener.Current.RequestAccessAsync();
        return status switch
        {
            UserNotificationListenerAccessStatus.Allowed => PermissionStatus.Allowed,
            UserNotificationListenerAccessStatus.Denied => PermissionStatus.Denied,
            _ => PermissionStatus.NotRequested
        };
    }

    public static void OpenSettings(string uri)
        => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    private static PermissionCapability ReadNotificationCapability(bool identity)
    {
        if (!identity)
            return new PermissionCapability(
                "notifications", "Notification listener", PermissionStatus.RequiresPackageIdentity,
                "Requires the signed MSIX identity and explicit user consent.", "ms-settings:privacy-notifications");
        try
        {
            var status = UserNotificationListener.Current.GetAccessStatus();
            return new PermissionCapability(
                "notifications", "Notification listener",
                status switch
                {
                    UserNotificationListenerAccessStatus.Allowed => PermissionStatus.Allowed,
                    UserNotificationListenerAccessStatus.Denied => PermissionStatus.Denied,
                    _ => PermissionStatus.NotRequested
                },
                "Reads notifications only after Windows grants consent.",
                "ms-settings:privacy-notifications");
        }
        catch (Exception exception)
        {
            return new PermissionCapability("notifications", "Notification listener", PermissionStatus.Unsupported, exception.Message);
        }
    }
}
