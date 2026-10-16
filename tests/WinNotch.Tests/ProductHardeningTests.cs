using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using WinNotch.Models;
using WinNotch.Platform.Windows.ProductHardening;
using WinNotch.Platform.Windows.Settings;
using WinNotch.ProductHardening;
using WinNotch.Services;
using WinNotch.Views;
using Xunit;

namespace WinNotch.Tests;

public sealed class ProductHardeningTests
{
    [Fact]
    public void SettingsTransfer_RoundTripsEnvelopeAndRejectsFutureFormat()
    {
        string root = TempRoot();
        try
        {
            var store = new JsonAppSettingsStore(Path.Combine(root, "active"), () => null);
            var transfer = new SettingsTransferService(store);
            var settings = new AppSettings { ShowCalendar = false };
            settings.ProductHardeningPreferences.Language = AppLanguage.SimplifiedChinese;
            string backup = transfer.Export(settings, Path.Combine(root, "backup.json"));

            var imported = transfer.Import(backup);

            Assert.False(imported.ShowCalendar);
            Assert.Equal(AppLanguage.SimplifiedChinese, imported.ProductHardeningPreferences.Language);
            Assert.Equal(1, imported.SettingsSchemaVersion);

            string future = Path.Combine(root, "future.json");
            File.WriteAllText(future, "{\"FormatVersion\":2,\"Settings\":{}}");
            Assert.Throws<InvalidOperationException>(() => transfer.Import(future));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CrashReporter_IsLocalBoundedAndCanBeDisabled()
    {
        string root = TempRoot();
        try
        {
            bool enabled = true;
            var reporter = new CrashReporter(() => enabled, root);
            for (int index = 0; index < 25; index++)
                Assert.NotNull(reporter.Report(new InvalidOperationException($"failure {index}"), "test", false));
            Assert.Equal(20, Directory.GetFiles(root, "crash-*.json").Length);
            enabled = false;
            Assert.Null(reporter.Report(new Exception("disabled"), "test", false));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PerformanceBudgetEvaluator_ReportsOnlyExceededBudgets()
    {
        var violations = PerformanceBudgets.Evaluate(new PerformanceSample(
            DateTimeOffset.UtcNow,
            300,
            5,
            500L * 1024 * 1024));

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, value => value.Budget == "UI dispatcher latency");
        Assert.Contains(violations, value => value.Budget == "Working set");
    }

    [Fact]
    public void UpdateAssetSelection_StaysWithinInstalledPackagingChannel()
    {
        string[] assets = ["WinNotch-1.2.3-Setup.exe", "WinNotch-1.2.3-x64.msix", "WinNotch.exe"];

        Assert.Equal("WinNotch-1.2.3-Setup.exe", UpdateService.SelectAssetName(assets, packaged: false));
        Assert.Equal("WinNotch-1.2.3-x64.msix", UpdateService.SelectAssetName(assets, packaged: true));
    }

    [Fact]
    public async Task PermissionHub_ReadsCapabilitiesWithoutRequestingPermission()
    {
        var capabilities = await new PermissionHubService().GetCapabilitiesAsync();

        Assert.Contains(capabilities, value => value.Id == "notifications");
        Assert.Contains(capabilities, value => value.Id == "camera");
        Assert.Contains(capabilities, value => value.Id == "microphone");
    }

    [Fact]
    public void Localization_TranslatesKnownStringsAndFallsBack()
    {
        var service = new LocalizationService();
        try
        {
            service.Apply(AppLanguage.SimplifiedChinese);
            Assert.Equal("设置", service.Translate("Settings"));
            Assert.Equal("Unlisted phrase", service.Translate("Unlisted phrase"));
        }
        finally { service.Apply(AppLanguage.English); }
    }

    [Fact]
    public void PackagingManifestAndReleaseWorkflow_DeclareIdentityCapabilitiesAndSigning()
    {
        string root = RepositoryRoot();
        string manifest = File.ReadAllText(Path.Combine(root, "packaging", "AppxManifest.xml.template"));
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));
        _ = XDocument.Parse(manifest.Replace("__PUBLISHER__", "CN=Test").Replace("__VERSION__", "1.0.0.0"));

        Assert.Contains("userNotificationListener", manifest);
        Assert.Contains("runFullTrust", manifest);
        Assert.Contains("WinNotchStartup", manifest);
        Assert.Contains("azure/trusted-signing-action", workflow);
        Assert.Contains("Build-Msix.ps1", workflow);
        Assert.Contains(".msix", workflow);
    }

    [Fact]
    public void AccessibilityResourcesAndIconButtons_ExposeKeyboardFocusAndNames()
    {
        string root = RepositoryRoot();
        string app = File.ReadAllText(Path.Combine(root, "src", "WinNotch", "App.xaml"));
        string notch = File.ReadAllText(Path.Combine(root, "src", "WinNotch", "Views", "NotchWindow.xaml"));
        string inline = File.ReadAllText(Path.Combine(root, "src", "WinNotch", "Views", "InlineSettingsView.xaml"));

        Assert.Contains("Accessibility.xaml", app);
        Assert.Contains("AutomationProperties.Name=\"Settings\"", notch);
        Assert.Contains("AutomationProperties.Name=\"Back\"", inline);
    }

    [Fact]
    public void HardeningWindow_BuildsOnStaThread()
    {
        Exception? failure = null;
        string root = TempRoot();
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                var store = new JsonAppSettingsStore(Path.Combine(root, "settings"), () => null);
                var reporter = new CrashReporter(() => true, Path.Combine(root, "crashes"));
                using var performance = new PerformanceBudgetMonitor(() => true);
                var window = new ProductHardeningWindow(
                    settings, store, new PermissionHubService(), reporter, performance);
                Assert.NotNull(window.Content);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Hardening UI test timed out.");
        try { Assert.Null(failure); }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "WinNotch.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static string TempRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "winnotch-hardening-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
