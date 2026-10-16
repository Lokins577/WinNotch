using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WinNotch.Models;
using WinNotch.Platform.Windows.ProductHardening;
using WinNotch.ProductHardening;
using WinNotch.Services;
using WinNotch.Settings;

namespace WinNotch.Views;

public partial class ProductHardeningWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _store;
    private readonly SettingsTransferService _transfer;
    private readonly PermissionHubService _permissions;
    private readonly CrashReporter _crashes;
    private readonly PerformanceBudgetMonitor _performance;
    private readonly InstallerMigrationService _migration = new();
    private InstallerMigrationStatus _migrationStatus;

    public ProductHardeningWindow(
        AppSettings settings,
        IAppSettingsStore store,
        PermissionHubService permissions,
        CrashReporter crashes,
        PerformanceBudgetMonitor performance)
    {
        _settings = settings;
        _store = store;
        _transfer = new SettingsTransferService(store);
        _permissions = permissions;
        _crashes = crashes;
        _performance = performance;
        InitializeComponent();
        LanguageBox.ItemsSource = Enum.GetValues<AppLanguage>();
        LanguageBox.SelectedItem = settings.ProductHardeningPreferences.Language;
        CrashReportsCheck.IsChecked = settings.ProductHardeningPreferences.LocalCrashReportsEnabled;
        PerformanceCheck.IsChecked = settings.ProductHardeningPreferences.PerformanceMonitoringEnabled;
        _migrationStatus = _migration.GetStatus();
        MigrationText.Text = _migrationStatus.Detail;
        LegacyUninstallButton.Visibility = _migrationStatus.IsPackaged && _migrationStatus.LegacyInstallerDetected
            ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) => await RefreshPermissionsAsync();
        RenderPerformance();
    }

    private async Task RefreshPermissionsAsync()
    {
        PermissionHost.Children.Clear();
        foreach (var permission in await _permissions.GetCapabilitiesAsync())
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = $"{permission.DisplayName} · {permission.Status}", FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = permission.Detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 10, 0) });
            grid.Children.Add(text);
            var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            if (permission.Id == "notifications" && permission.Status == PermissionStatus.NotRequested)
            {
                var request = new Button { Content = "Request access", Padding = new Thickness(8, 3, 8, 3) };
                request.Click += async (_, _) =>
                {
                    StatusText.Text = (await _permissions.RequestNotificationAccessAsync()).ToString();
                    await RefreshPermissionsAsync();
                };
                actions.Children.Add(request);
            }
            if (permission.SettingsUri != null)
            {
                var open = new Button { Content = "Open settings", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(5, 0, 0, 0), Tag = permission.SettingsUri };
                open.Click += (_, _) => PermissionHubService.OpenSettings((string)open.Tag);
                actions.Children.Add(open);
            }
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
            PermissionHost.Children.Add(new Border { Padding = new Thickness(10), Child = grid, BorderThickness = new Thickness(1), BorderBrush = System.Windows.Media.Brushes.Gray, CornerRadius = new CornerRadius(6) });
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Export WinNotch settings", FileName = $"WinNotch-settings-{DateTime.Now:yyyyMMdd}.json", Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { StatusText.Text = "Exported " + _transfer.Export(_settings, dialog.FileName); }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import WinNotch settings", Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        if (MessageBox.Show("Replace saved settings with this reviewed backup? A restart is required.", "Import settings", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            _transfer.Import(dialog.FileName);
            StatusText.Text = "Settings imported. Restart WinNotch to apply every module safely.";
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void OnLegacyUninstall(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Remove the legacy Inno installation? Shared AppData settings are not deleted unless the legacy uninstaller asks and you explicitly agree.", "Installer migration", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _migration.RemoveLegacyStartupEntry();
        _migration.StartLegacyUninstaller(_migrationStatus);
    }

    private void OnOpenCrashFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_crashes.DirectoryPath);
        Process.Start(new ProcessStartInfo(_crashes.DirectoryPath) { UseShellExecute = true });
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var value = _settings.ProductHardeningPreferences;
        value.Language = LanguageBox.SelectedItem is AppLanguage language ? language : AppLanguage.Automatic;
        value.LocalCrashReportsEnabled = CrashReportsCheck.IsChecked == true;
        value.PerformanceMonitoringEnabled = PerformanceCheck.IsChecked == true;
        _store.Save(_settings);
        StatusText.Text = "Saved. Restart to fully apply language changes.";
        RenderPerformance();
    }

    private void RenderPerformance()
    {
        var sample = _performance.Latest;
        PerformanceText.Text = sample.CapturedAt == DateTimeOffset.MinValue
            ? "Waiting for the first performance sample."
            : $"Latest: UI {sample.UiLatencyMilliseconds:0} ms · CPU {sample.ProcessCpuPercent:0.0}% · Working set {sample.WorkingSetBytes / 1024d / 1024:0} MiB";
    }
}
