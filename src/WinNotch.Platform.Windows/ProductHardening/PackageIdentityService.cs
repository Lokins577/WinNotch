using Windows.ApplicationModel;

namespace WinNotch.Platform.Windows.ProductHardening;

public static class PackageIdentityService
{
    public static bool HasIdentity
    {
        get
        {
            try { return !string.IsNullOrWhiteSpace(Package.Current.Id.Name); }
            catch { return false; }
        }
    }

    public static string? PackageName
    {
        get
        {
            try { return Package.Current.Id.Name; }
            catch { return null; }
        }
    }
}
