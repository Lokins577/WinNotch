using System.Windows;
using WinNotch.Models;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private void OpenFeatureCatalog()
    {
        var window = new FeatureCatalogWindow(_settings)
        {
            Owner = this,
            Topmost = true
        };
        window.Show();
        window.Activate();
        window.Topmost = false;
    }

    public void OpenSettings()
    {
        // First ensure the notch is open
        if (_vm.NotchState != NotchState.Open)
            TransitionToOpen();

        ExpandSettings();
    }

    private void OnSettingsGearClick(object sender, RoutedEventArgs e)
    {
        if (_isSettingsExpanded)
            CollapseSettings();
        else
            ExpandSettings();
    }

    private void ExpandSettings()
    {
        _isSettingsExpanded = true;

        if (_dock == NotchDock.Top)
        {
            // Top dock: Load settings and grow the notch taller (slide down)
            InlineSettings.LoadSettings(_settings, _settingsStore);
            InlineSettings.Visibility = Visibility.Visible;

            double targetH = GetTopSettingsHeight();
            _heightSpring.Response = 0.42;
            _heightSpring.DampingFraction = 0.80;
            _heightSpring.AnimateTo(targetH, _currentHeight);
        }
        else
        {
            // Side dock: Load settings in vertical panel and expand width
            VerticalInlineSettings.LoadSettings(_settings, _settingsStore);
            VerticalSettingsPanel.Visibility = Visibility.Visible;

            double targetW = NotchConstants.SideOpenWidth + 280 + NotchConstants.OpenTopRadius;
            _widthSpring.Response = 0.42;
            _widthSpring.DampingFraction = 0.80;
            _widthSpring.AnimateTo(targetW, _currentWidth);
        }
    }

    private void CollapseSettings()
    {
        _isSettingsExpanded = false;

        if (_dock == NotchDock.Top)
        {
            // Top dock: Hide settings and shrink back to normal open height
            InlineSettings.Visibility = Visibility.Collapsed;

            double targetH = GetTopOpenHeight();
            _heightSpring.Response = 0.42;
            _heightSpring.DampingFraction = 0.80;
            _heightSpring.AnimateTo(targetH, _currentHeight);
        }
        else
        {
            // Side dock: Hide settings panel and shrink width back
            VerticalSettingsPanel.Visibility = Visibility.Collapsed;

            _widthSpring.Response = 0.42;
            _widthSpring.DampingFraction = 0.80;
            _widthSpring.AnimateTo(NotchConstants.SideOpenWidth, _currentWidth);
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;

        // Apply liquid glass theme if toggled
        _themeService.Apply(settings);
        ApplyLiquidGlassEffect(settings.UseLiquidGlassTheme);

        // Toggle visibility of components based on settings
        LiveActivity.UserEnabled = settings.ShowMusicControls;
        MusicPlayer.Visibility = settings.ShowMusicControls ? Visibility.Visible : Visibility.Collapsed;
        BatteryIndicator.UserEnabled = settings.ShowBattery;
        CalendarPanel.Visibility = settings.ShowCalendar ? Visibility.Visible : Visibility.Collapsed;
        CompactVisualizer_SetVisible(settings.ShowVisualizer);
        UpdateClosedContentVisibility();

        // HUD gates (external volume/brightness changes must respect these too)
        HudOverlay.VolumeHudEnabled = settings.ShowVolumeHud;
        HudOverlay.BrightnessHudEnabled = settings.ShowBrightnessHud;
        VerticalHudOverlay.VolumeHudEnabled = settings.ShowVolumeHud;
        VerticalHudOverlay.BrightnessHudEnabled = settings.ShowBrightnessHud;

        // Webcam
        _webcamService.SetTargetFps(settings.WebcamFps);
        if (settings.ShowWebcam)
        {
            WebcamPanel.Visibility = Visibility.Visible;
            WebcamMirror.Bind(_webcamService);
            if (_webcamService.MaxCameraFps > 0)
            {
                InlineSettings.UpdateWebcamFpsMax(_webcamService.MaxCameraFps);
                VerticalInlineSettings.UpdateWebcamFpsMax(_webcamService.MaxCameraFps);
            }
        }
        else
        {
            WebcamPanel.Visibility = Visibility.Collapsed;
            WebcamMirror.StopCamera();
        }

        // Re-adjust notch size if currently open
        if (_vm.NotchState == NotchState.Open)
        {
            if (_dock == NotchDock.Top)
            {
                _widthSpring.AnimateTo(GetOpenWidth(), _currentWidth);

                if (!_isSettingsExpanded)
                {
                    double targetH = GetTopOpenHeight();
                    _heightSpring.AnimateTo(targetH, _currentHeight);
                }
            }
            // Side docks: don't re-animate width/height — settings panel has fixed size
        }

        UpdateSpaceReservation();

        // Re-target the shadow for the current state (ShowShadow may have toggled)
        if (!_isDragging)
        {
            _shadowOpacitySpring.AnimateTo(
                _vm.NotchState == NotchState.Open ? OpenShadowOpacity : ClosedShadowOpacity,
                _currentShadowOpacity);
        }
    }

    private double GetOpenWidth()
    {
        double accessoryWidth = GetMeasuredPanelWidth(PluginAccessoryPanel, NotchConstants.OpenHeight);
        bool cal = _settings.ShowCalendar;
        bool cam = _settings.ShowWebcam;

        double baseWidth = NotchConstants.OpenWidth;
        if (cal && cam)
            baseWidth = NotchConstants.OpenWidthWithCalendarAndWebcam;
        else if (cal)
            baseWidth = NotchConstants.OpenWidthWithCalendar;
        else if (cam)
            baseWidth = NotchConstants.OpenWidthWithWebcam;

        return baseWidth + accessoryWidth;
    }
}
