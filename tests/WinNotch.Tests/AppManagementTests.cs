using System.IO;
using WinNotch.AppManagement;
using WinNotch.Models;
using WinNotch.Platform.Windows.AppManagement;
using WinNotch.Services;
using WinNotch.Settings;
using WinNotch.Views;
using Xunit;

namespace WinNotch.Tests;

public sealed class AppManagementTests
{
    [Fact]
    public void WingetParser_ParsesPreviewTableWithoutDependingOnHeaderLanguage()
    {
        const string output = """
Name                           Id                         Version      Available    Source
---------------------------------------------------------------------------------------
PowerToys (Preview)            Microsoft.PowerToys       0.80.0       0.81.0       winget
Visual Studio Code             Microsoft.VisualStudioCode 1.90.0      1.91.0       winget
2 upgrades available.
""";

        var upgrades = WingetOutputParser.ParseUpgrades(output);

        Assert.Equal(2, upgrades.Count);
        Assert.Equal("Microsoft.PowerToys", upgrades[0].PackageId);
        Assert.Equal("0.81.0", upgrades[0].AvailableVersion);
    }

    [Fact]
    public async Task WingetDiscovery_UsesReadOnlyArgumentsAndInjectedRunner()
    {
        IReadOnlyList<string>? actual = null;
        var service = new WingetService((arguments, _) =>
        {
            actual = arguments;
            return Task.FromResult(new WingetService.CommandResult(0, "", ""));
        });

        await service.DiscoverUpgradesAsync();

        Assert.NotNull(actual);
        Assert.Equal("upgrade", actual![0]);
        Assert.DoesNotContain("--all", actual);
    }

    [Fact]
    public void CleanupScan_OnlyReturnsOldFilesInsideConfiguredRoots()
    {
        string root = TempRoot();
        try
        {
            string old = Path.Combine(root, "old.tmp");
            string recent = Path.Combine(root, "recent.tmp");
            File.WriteAllText(old, "old");
            File.WriteAllText(recent, "recent");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10));
            var service = new CleanupService(
                new AppManagementPreferences(),
                [(root, "Test", 7)]);

            var candidates = service.Scan();

            Assert.Equal(old, Assert.Single(candidates).Path);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ProcessPolicy_ProtectsSystemAndOwnProcess()
    {
        Assert.False(ProcessTerminationPolicy.CanTarget(Environment.ProcessId, "test", out _));
        Assert.False(ProcessTerminationPolicy.CanTarget(500, "lsass", out _));
        Assert.True(ProcessTerminationPolicy.CanTarget(500, "ordinary-app", out _));
    }

    [Fact]
    public void WindowsInventories_AreReadOnlyAndReturnValidRecords()
    {
        var applications = new InstalledApplicationService().GetInstalledApplications();
        var ports = new PortInspectionService().GetPorts();

        Assert.All(applications, app => Assert.False(string.IsNullOrWhiteSpace(app.Name)));
        Assert.All(ports, port =>
        {
            Assert.InRange(port.LocalPort, 0, 65535);
            Assert.True(port.ProcessId >= 0);
        });
    }

    [Fact]
    public void InstallerHandoff_RejectsUnknownExtensionBeforeLaunching()
    {
        string root = TempRoot();
        try
        {
            string file = Path.Combine(root, "payload.txt");
            File.WriteAllText(file, "fixture");
            Assert.Throws<InvalidOperationException>(() =>
                new InstalledApplicationService().StartInstaller(file));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ManagementWindow_BuildsOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings();
                using var service = new AppManagementFeatureService(settings.AppManagementPreferences);
                var window = new AppManagementWindow(service, settings, new MemoryStore());
                Assert.NotNull(window.Content);
                window.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "App management UI test timed out.");
        Assert.Null(failure);
    }

    private static string TempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "winnotch-app-management-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class MemoryStore : IAppSettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
        public void ApplyStartOnBoot(bool enabled) { }
    }
}
