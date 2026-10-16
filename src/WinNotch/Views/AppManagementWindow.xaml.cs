using System.IO;
using System.Windows;
using System.Windows.Controls;
using WinNotch.AppManagement;
using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Settings;
using Forms = System.Windows.Forms;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace WinNotch.Views;

public partial class AppManagementWindow : Window
{
    private readonly AppManagementFeatureService _service;
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _store;
    private CancellationTokenSource? _operationCancellation;

    public AppManagementWindow(
        AppManagementFeatureService service,
        AppSettings settings,
        IAppSettingsStore store)
    {
        _service = service;
        _settings = settings;
        _store = store;
        InitializeComponent();
        TempAgeText.Text = settings.AppManagementPreferences.TemporaryFileMinimumAgeDays.ToString();
        RetentionText.Text = settings.AppManagementPreferences.MessagingRetentionDays.ToString();
        RefreshInstalled();
        RefreshPorts();
    }

    private async void OnDiscoverUpgrades(object sender, RoutedEventArgs e)
    {
        CancelOperation();
        _operationCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        StatusText.Text = "Querying WinGet without installing anything…";
        var result = await _service.Winget.DiscoverUpgradesAsync(_operationCancellation.Token);
        UpgradeList.ItemsSource = result.Upgrades.Select(value => new UpgradeRow(
            value, $"{value.Name} · {value.InstalledVersion} → {value.AvailableVersion} · {value.PackageId}"));
        StatusText.Text = result.Error ?? $"Found {result.Upgrades.Count} upgrades.";
    }

    private void OnUpgrade(object sender, RoutedEventArgs e)
    {
        if (UpgradeList.SelectedItem is not UpgradeRow row) return;
        if (MessageBox.Show(
                $"Launch WinGet's interactive installer?\n\n{row.Value.Name}\n{row.Value.InstalledVersion} → {row.Value.AvailableVersion}\nID: {row.Value.PackageId}",
                "Review upgrade", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _service.Winget.StartUpgrade(row.Value);
        StatusText.Text = "WinGet upgrade launched. WinNotch does not suppress installer UI.";
    }

    private void OnRefreshInstalled(object sender, RoutedEventArgs e) => RefreshInstalled();
    private void RefreshInstalled()
    {
        var values = _service.InstalledApps.GetInstalledApplications();
        InstalledList.ItemsSource = values.Select(value => new InstalledRow(
            value, $"{value.Name} · {value.Version ?? "unknown"} · {value.Publisher ?? "unknown publisher"}"));
        StatusText.Text = $"Loaded {values.Count} uninstall registrations.";
    }

    private void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (InstalledList.SelectedItem is not InstalledRow row) return;
        if (string.IsNullOrWhiteSpace(row.Value.UninstallCommand))
        {
            StatusText.Text = "The selected application exposes no uninstall command.";
            return;
        }
        if (MessageBox.Show(
                $"Launch this application's registered interactive uninstaller?\n\n{row.Value.Name}\n{row.Value.Version}\n{row.Value.Publisher}\n\nCommand:\n{row.Value.UninstallCommand}",
                "Review uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _service.InstalledApps.StartUninstaller(row.Value);
        StatusText.Text = "Registered uninstaller launched.";
    }

    private void OnInstaller(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose installer",
            Filter = "Windows installers|*.exe;*.msi;*.msix;*.msixbundle;*.appinstaller|All files|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        var info = new FileInfo(dialog.FileName);
        if (MessageBox.Show(
                $"Launch installer with Windows shell?\n\n{info.Name}\n{FormatBytes(info.Length)}\n{info.DirectoryName}",
                "Review installer", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _service.InstalledApps.StartInstaller(info.FullName);
        StatusText.Text = "Installer handed to Windows.";
    }

    private void OnAddMessagingFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose a messaging download folder for retention review",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        var preferences = SaveCleanupPreferences();
        if (!preferences.MessagingDownloadFolders.Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase))
            preferences.MessagingDownloadFolders.Add(dialog.SelectedPath);
        preferences.Normalize();
        _store.Save(_settings);
        StatusText.Text = "Messaging folder added. Scan remains read-only until files are selected for recycling.";
    }

    private async void OnScanCleanup(object sender, RoutedEventArgs e)
    {
        SaveCleanupPreferences();
        CancelOperation();
        _operationCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        StatusText.Text = "Scanning configured roots without deleting…";
        try
        {
            var values = await _service.Cleanup.ScanAsync(_operationCancellation.Token);
            CleanupList.ItemsSource = values.Select(value => new CleanupRow(
                value, $"{value.Category} · {FormatBytes(value.SizeBytes)} · {value.LastModified.LocalDateTime:g} · {value.Path}"));
            StatusText.Text = $"Found {values.Count} recoverable file candidates totaling {FormatBytes(values.Sum(value => value.SizeBytes))}.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Cleanup scan cancelled."; }
    }

    private void OnRecycle(object sender, RoutedEventArgs e)
    {
        var selected = CleanupList.SelectedItems.Cast<CleanupRow>().ToArray();
        if (selected.Length == 0) return;
        long bytes = selected.Sum(value => value.Value.SizeBytes);
        if (MessageBox.Show(
                $"Move {selected.Length} selected files ({FormatBytes(bytes)}) to the Recycle Bin?\n\nNo unselected file or directory will be changed.",
                "Review cleanup", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var result = _service.Cleanup.Recycle(selected.Select(value => value.Value));
        StatusText.Text = $"Recycled {result.Recycled}/{selected.Length}. {string.Join(" ", result.Failures.Take(3))}";
        OnScanCleanup(sender, e);
    }

    private AppManagementPreferences SaveCleanupPreferences()
    {
        var preferences = _settings.AppManagementPreferences;
        if (int.TryParse(TempAgeText.Text, out int temporary)) preferences.TemporaryFileMinimumAgeDays = temporary;
        if (int.TryParse(RetentionText.Text, out int retention)) preferences.MessagingRetentionDays = retention;
        preferences.Normalize();
        _store.Save(_settings);
        return preferences;
    }

    private void OnRefreshPorts(object sender, RoutedEventArgs e) => RefreshPorts();
    private void RefreshPorts()
    {
        var values = _service.Ports.GetPorts();
        PortList.ItemsSource = values.Select(value => new PortRow(
            value, $"{value.Protocol} {value.LocalAddress}:{value.LocalPort} → {value.RemoteAddress}:{value.RemotePort} · {value.State} · {value.ProcessName} ({value.ProcessId})"));
        StatusText.Text = $"Loaded {values.Count} TCP/UDP rows.";
    }

    private void OnRequestClose(object sender, RoutedEventArgs e)
    {
        if (PortList.SelectedItem is not PortRow row) return;
        var result = _service.Processes.RequestClose(row.Value.ProcessId);
        StatusText.Text = result.Message;
    }

    private void OnForceTerminate(object sender, RoutedEventArgs e)
    {
        if (PortList.SelectedItem is not PortRow row) return;
        if (!ProcessTerminationPolicy.CanTarget(row.Value.ProcessId, row.Value.ProcessName, out string reason))
        {
            StatusText.Text = reason;
            return;
        }
        string? confirmation = PromptForText(
            "Force termination can discard unsaved work. Type the exact process name to continue:",
            row.Value.ProcessName);
        if (!string.Equals(confirmation, row.Value.ProcessName, StringComparison.Ordinal))
        {
            StatusText.Text = "Force termination cancelled.";
            return;
        }
        StatusText.Text = _service.Processes.ForceTerminate(row.Value.ProcessId).Message;
    }

    private string? PromptForText(string prompt, string expected)
    {
        var text = new WpfTextBox { Margin = new Thickness(0, 8, 0, 8) };
        var ok = new Button { Content = "Confirm", Padding = new Thickness(10, 4, 10, 4), IsDefault = true };
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(text);
        panel.Children.Add(ok);
        var window = new Window { Title = $"Confirm {expected}", Width = 430, Height = 180, Owner = this, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true ? text.Text : null;
    }

    private void CancelOperation()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        CancelOperation();
        base.OnClosed(e);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    private sealed record UpgradeRow(PackageUpgrade Value, string Display);
    private sealed record InstalledRow(InstalledApplication Value, string Display);
    private sealed record CleanupRow(CleanupCandidate Value, string Display);
    private sealed record PortRow(PortOwner Value, string Display);
}
