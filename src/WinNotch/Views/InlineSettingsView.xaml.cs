using System;
using System.Windows;
using WinNotch.Helpers;
using WinNotch.Features;
using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Settings;
using Forms = System.Windows.Forms;

namespace WinNotch.Views;

public partial class InlineSettingsView : System.Windows.Controls.UserControl
{
    private AppSettings? _settings;
    private IAppSettingsStore? _settingsStore;
    private bool _isLoading;
    private readonly UpdateService _updateService = new(AppInfo.Version);

    /// <summary>Fired when any setting changes.</summary>
    public event Action<AppSettings>? SettingsChanged;

    /// <summary>Fired when the user clicks the back arrow.</summary>
    public event Action? BackRequested;

    /// <summary>Fired when theme-related appearance changes.</summary>
    public event Action<AppSettings>? ThemeChangeRequested;

    /// <summary>Fired when the user wants to manage plugins.</summary>
    public event Action? ManagePluginsRequested;

    /// <summary>Fired when the user wants to inspect built-in feature availability.</summary>
    public event Action? ManageFeaturesRequested;

    public event Action<AppSettings>? DownloadSettingsChanged;
    public event Action<AppSettings>? LyricsSettingsChanged;

    public InlineSettingsView()
    {
        InitializeComponent();
        VersionText.Text = AppInfo.NameWithVersion;
    }

    public void LoadSettings(AppSettings settings, IAppSettingsStore settingsStore)
    {
        _isLoading = true;
        _settings = settings;
        _settingsStore = settingsStore;

        StartOnBootCheck.IsChecked = settings.StartOnBoot;
        ShowShadowCheck.IsChecked = settings.ShowShadow;
        ShowMusicCheck.IsChecked = settings.ShowMusicControls;
        ShowVisualizerCheck.IsChecked = settings.ShowVisualizer;
        VolumeHudCheck.IsChecked = settings.ShowVolumeHud;
        BrightnessHudCheck.IsChecked = settings.ShowBrightnessHud;
        ShowBatteryCheck.IsChecked = settings.ShowBattery;
        ShowCalendarCheck.IsChecked = settings.ShowCalendar;
        ShowWebcamCheck.IsChecked = settings.ShowWebcam;
        WebcamFpsSlider.Value = settings.WebcamFps;
        WebcamFpsLabel.Text = $"{settings.WebcamFps}";
        LiquidGlassCheck.IsChecked = settings.UseLiquidGlassTheme;
        OnlineLyricsCheck.IsChecked = settings.OnlineLyricsEnabled;
        AdaptToWindowsThemeCheck.IsChecked = settings.AdaptToWindowsTheme;
        ShowOnAllScreensCheck.IsChecked = settings.ShowOnAllScreens;
        SyncIslandsCheck.IsChecked = settings.SyncIslandsOpenState;
        ReserveSpaceCheck.IsChecked = settings.ReserveSpaceAtTop;
        DownloadMonitoringCheck.IsChecked = settings.IsFeatureAvailable(BuiltInFeatures.Downloads);
        DownloadsFolderText.Text = string.IsNullOrWhiteSpace(settings.DownloadsFolder)
            ? "No folder selected"
            : settings.DownloadsFolder;
        ClipboardImagesCheck.IsChecked = settings.ClipboardPreferences.CaptureImages;
        ClipboardFilesCheck.IsChecked = settings.ClipboardPreferences.CaptureFiles;
        ClipboardLimitSlider.Value = settings.ClipboardPreferences.MaximumItems;
        ClipboardIgnoredAppsText.Text = string.Join(", ", settings.ClipboardPreferences.IgnoredApplications);

        if (settings.HoverMode == HoverMode.LongHoverOpen)
            LongHoverRadio.IsChecked = true;
        else
            HoverPeekRadio.IsChecked = true;

        if (settings.GetManualTheme() == AppTheme.Light)
            ThemeLightRadio.IsChecked = true;
        else
            ThemeDarkRadio.IsChecked = true;

        _isLoading = false;
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null) return;

        _settings.StartOnBoot = StartOnBootCheck.IsChecked == true;
        _settings.ShowShadow = ShowShadowCheck.IsChecked == true;
        _settings.ShowMusicControls = ShowMusicCheck.IsChecked == true;
        _settings.ShowVisualizer = ShowVisualizerCheck.IsChecked == true;
        _settings.ShowVolumeHud = VolumeHudCheck.IsChecked == true;
        _settings.ShowBrightnessHud = BrightnessHudCheck.IsChecked == true;
        _settings.ShowBattery = ShowBatteryCheck.IsChecked == true;
        _settings.ShowCalendar = ShowCalendarCheck.IsChecked == true;
        _settings.ShowWebcam = ShowWebcamCheck.IsChecked == true;
        _settings.ShowOnAllScreens = ShowOnAllScreensCheck.IsChecked == true;
        _settings.SyncIslandsOpenState = SyncIslandsCheck.IsChecked == true;
        _settings.ReserveSpaceAtTop = ReserveSpaceCheck.IsChecked == true;

        _settingsStore!.ApplyStartOnBoot(_settings.StartOnBoot);
        _settingsStore.Save(_settings);
        SettingsChanged?.Invoke(_settings);
    }

    private void OnHoverModeChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null) return;

        _settings.HoverMode = LongHoverRadio.IsChecked == true
            ? HoverMode.LongHoverOpen
            : HoverMode.HoverPeekClickOpen;

        _settingsStore!.Save(_settings);
        SettingsChanged?.Invoke(_settings);
    }

    private void OnWebcamFpsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading || _settings == null) return;
        _settings.WebcamFps = (int)e.NewValue;
        WebcamFpsLabel.Text = $"{_settings.WebcamFps}";
        _settingsStore!.Save(_settings);
        SettingsChanged?.Invoke(_settings);
    }

    public void UpdateWebcamFpsMax(double maxFps)
    {
        if (maxFps > 5)
            WebcamFpsSlider.Maximum = Math.Min(maxFps, 60);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }

    private void OnThemeChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null) return;

        // Picking a manual theme should immediately opt out of Windows-follow mode.
        _settings.AdaptToWindowsTheme = false;
        AdaptToWindowsThemeCheck.IsChecked = false;
        _settings.Theme = ThemeLightRadio.IsChecked == true ? AppTheme.Light : AppTheme.Dark;

        _settingsStore!.Save(_settings);
        ThemeChangeRequested?.Invoke(_settings);
    }

    private void OnAdaptThemeChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null) return;

        _settings.AdaptToWindowsTheme = AdaptToWindowsThemeCheck.IsChecked == true;
        _settingsStore!.Save(_settings);
        ThemeChangeRequested?.Invoke(_settings);
    }

    private void OnLiquidGlassChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null) return;

        _settings.UseLiquidGlassTheme = LiquidGlassCheck.IsChecked == true;
        _settingsStore!.Save(_settings);
        SettingsChanged?.Invoke(_settings);
    }

    private void OnManagePluginsClick(object sender, RoutedEventArgs e)
    {
        ManagePluginsRequested?.Invoke();
    }

    private void OnOnlineLyricsChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null || _settingsStore == null) return;
        _settings.OnlineLyricsEnabled = OnlineLyricsCheck.IsChecked == true;
        _settingsStore.Save(_settings);
        LyricsSettingsChanged?.Invoke(_settings);
    }

    private void OnManageFeaturesClick(object sender, RoutedEventArgs e)
    {
        ManageFeaturesRequested?.Invoke();
    }

    private void OnDownloadMonitoringChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null || _settingsStore == null) return;

        _settings.SetFeatureAvailable(
            BuiltInFeatures.Downloads,
            DownloadMonitoringCheck.IsChecked == true);
        _settingsStore.Save(_settings);
        DownloadSettingsChanged?.Invoke(_settings);
    }

    private void OnChooseDownloadsFolder(object sender, RoutedEventArgs e)
    {
        if (_settings == null || _settingsStore == null) return;

        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose the folder WinNotch should monitor for downloads",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = _settings.DownloadsFolder ?? string.Empty
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;

        _settings.DownloadsFolder = dialog.SelectedPath;
        _settings.SetFeatureAvailable(BuiltInFeatures.Downloads, true);
        _isLoading = true;
        DownloadMonitoringCheck.IsChecked = true;
        DownloadsFolderText.Text = dialog.SelectedPath;
        _isLoading = false;
        _settingsStore.Save(_settings);
        DownloadSettingsChanged?.Invoke(_settings);
    }

    private void OnClipboardSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading || _settings == null || _settingsStore == null) return;
        _settings.ClipboardPreferences.CaptureImages = ClipboardImagesCheck.IsChecked == true;
        _settings.ClipboardPreferences.CaptureFiles = ClipboardFilesCheck.IsChecked == true;
        _settings.ClipboardPreferences.MaximumItems = (int)ClipboardLimitSlider.Value;
        _settings.ClipboardPreferences.IgnoredApplications = ClipboardIgnoredAppsText.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _settings.ClipboardPreferences.Normalize();
        _settingsStore.Save(_settings);
    }

    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        CheckUpdateText.Text = "Checking...";
        CheckUpdateButton.IsEnabled = false;

        var result = await _updateService.CheckForUpdateAsync();

        switch (result)
        {
            case UpdateCheckResult.UpdateAvailable:
                CheckUpdateButton.Visibility = Visibility.Collapsed;
                UpdateButton.Visibility = Visibility.Visible;
                UpdateButtonText.Text = $"⬇ Update to v{_updateService.LatestVersion}";
                break;
            case UpdateCheckResult.CheckFailed:
                CheckUpdateText.Text = "Couldn't check — see log";
                break;
            default:
                CheckUpdateText.Text = "You're up to date ✓";
                break;
        }

        CheckUpdateButton.IsEnabled = true;
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        UpdateButtonText.Text = "Downloading...";
        UpdateButton.IsEnabled = false;

        var progress = new Progress<double>(p =>
        {
            var pct = (int)(p * 100);
            UpdateButtonText.Text = $"Downloading... {pct}%";
        });

        var (ok, error) = await _updateService.DownloadAndLaunchInstallerAsync(progress);
        if (!ok)
        {
            UpdateButtonText.Text = error ?? "Update failed — retry";
            UpdateButton.IsEnabled = true;
        }
    }
}
