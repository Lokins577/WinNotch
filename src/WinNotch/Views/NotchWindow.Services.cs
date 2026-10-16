using System;
using System.Windows;
using System.Windows.Threading;
using WinNotch.Helpers;
using WinNotch.Features;
using WinNotch.Models;

namespace WinNotch.Views;

public partial class NotchWindow
{
    private async System.Threading.Tasks.Task InitializeServicesAsync()
    {
        // Global one-time init happens once app-wide; every island awaits it
        await _services.EnsureInitializedAsync(_settings);
        AttachCommandBar();
        AttachQuickPanel();
        AttachRadialMenu();
        AttachWindowManagement();

        // Bind views to services
        MusicPlayer.Bind(_mediaService, _services.Lyrics);
        LiveActivity.Bind(_mediaService, _audioCaptureService);

        // Inline settings
        InlineSettings.SettingsChanged += s =>
        {
            ApplySettings(s);
            SettingsUpdatedByUser?.Invoke(s);
        };
        InlineSettings.BackRequested += CollapseSettings;
        InlineSettings.ThemeChangeRequested += async themeSettings =>
        {
            _settings = themeSettings;
            await _themeService.ApplyWithTransition(themeSettings, NotchCanvas);
            SettingsUpdatedByUser?.Invoke(themeSettings);
        };

        // Vertical inline settings (separate instance for side dock)
        VerticalInlineSettings.SettingsChanged += s =>
        {
            ApplySettings(s);
            SettingsUpdatedByUser?.Invoke(s);
        };
        VerticalInlineSettings.BackRequested += CollapseSettings;
        VerticalInlineSettings.ThemeChangeRequested += async themeSettings =>
        {
            _settings = themeSettings;
            await _themeService.ApplyWithTransition(themeSettings, NotchCanvas);
            SettingsUpdatedByUser?.Invoke(themeSettings);
        };
        InlineSettings.ManageFeaturesRequested += OpenFeatureCatalog;
        VerticalInlineSettings.ManageFeaturesRequested += OpenFeatureCatalog;
        InlineSettings.DownloadSettingsChanged += ApplyDownloadSettingsAsync;
        VerticalInlineSettings.DownloadSettingsChanged += ApplyDownloadSettingsAsync;
        InlineSettings.LyricsSettingsChanged += ApplyLyricsSettings;
        VerticalInlineSettings.LyricsSettingsChanged += ApplyLyricsSettings;
        InlineSettings.ClipboardSettingsChanged += ApplyClipboardSettings;
        VerticalInlineSettings.ClipboardSettingsChanged += ApplyClipboardSettings;
        InlineSettings.CommandBarSettingsRequested += OpenCommandBarSettings;
        VerticalInlineSettings.CommandBarSettingsRequested += OpenCommandBarSettings;
        InlineSettings.RadialMenuSettingsRequested += OpenRadialMenuSettings;
        VerticalInlineSettings.RadialMenuSettingsRequested += OpenRadialMenuSettings;

        // Toggle clock/live-activity visibility based on music state
        _mediaService.MediaInfo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MediaInfo.HasMedia) or nameof(MediaInfo.IsPlaying))
                Dispatcher.Invoke(UpdateClosedContentVisibility);
        };
        _mediaService.SessionChanged += () => Dispatcher.Invoke(UpdateClosedContentVisibility);
        UpdateClosedContentVisibility();

        // Volume & Brightness
        HudOverlay.Bind(_volumeService, _brightnessService);
        VerticalHudOverlay.IsActive = false;
        VerticalHudOverlay.Bind(_volumeService, _brightnessService);

        // Battery
        BatteryIndicator.Bind(_batteryService);

        // Timers, focus cycles, and stopwatch
        bool timersAvailable = _settings.IsFeatureAvailable(BuiltInFeatures.Timers);
        TimerOpenView.Visibility = timersAvailable ? Visibility.Visible : Visibility.Collapsed;
        VerticalTimerView.Visibility = timersAvailable ? Visibility.Visible : Visibility.Collapsed;
        if (timersAvailable && _services.Timer is { } timer)
        {
            TimerOpenView.Bind(timer);
            VerticalTimerView.Bind(timer, compact: true);
            TimerCompact.Bind(timer);
        }

        // Download folder live activity
        bool downloadsAvailable = _settings.IsFeatureAvailable(BuiltInFeatures.Downloads);
        DownloadOpenView.Visibility = downloadsAvailable ? Visibility.Visible : Visibility.Collapsed;
        VerticalDownloadView.Visibility = downloadsAvailable ? Visibility.Visible : Visibility.Collapsed;
        if (downloadsAvailable && _services.Downloads is { } downloads)
        {
            DownloadOpenView.Bind(downloads);
            VerticalDownloadView.Bind(downloads, compact: true);
            DownloadCompact.Bind(downloads);
        }

        // Windows notification inbox (permission gated)
        bool notificationsAvailable = _settings.IsFeatureAvailable(BuiltInFeatures.Notifications);
        NotificationOpenView.Visibility = notificationsAvailable ? Visibility.Visible : Visibility.Collapsed;
        VerticalNotificationView.Visibility = notificationsAvailable ? Visibility.Visible : Visibility.Collapsed;
        if (notificationsAvailable && _services.Notifications is { } notifications)
        {
            NotificationOpenView.Bind(notifications);
            VerticalNotificationView.Bind(notifications, compact: true);
            NotificationCompact.Bind(notifications);
        }

        // Local AI agent activity
        bool agentsAvailable = _settings.IsFeatureAvailable(BuiltInFeatures.AgentActivity);
        AgentOpenView.Visibility = agentsAvailable ? Visibility.Visible : Visibility.Collapsed;
        VerticalAgentView.Visibility = agentsAvailable ? Visibility.Visible : Visibility.Collapsed;
        if (agentsAvailable && _services.Agents is { } agents)
        {
            AgentOpenView.Bind(agents);
            VerticalAgentView.Bind(agents, compact: true);
            AgentCompact.Bind(agents);
        }

        // Persistent clipboard history
        bool clipboardAvailable = _settings.IsFeatureAvailable(BuiltInFeatures.Clipboard);
        ClipboardOpenView.Visibility = clipboardAvailable ? Visibility.Visible : Visibility.Collapsed;
        VerticalClipboardView.Visibility = clipboardAvailable ? Visibility.Visible : Visibility.Collapsed;
        if (clipboardAvailable && _services.ClipboardHistory is { } clipboard)
        {
            ClipboardOpenView.Bind(clipboard);
            VerticalClipboardView.Bind(clipboard, compact: true);
            ClipboardCompact.Bind(clipboard);
        }

        // Calendar
        CalendarPanel_View.Bind(_calendarService);
        CalendarPanel.Visibility = _settings.ShowCalendar ? Visibility.Visible : Visibility.Collapsed;

        // File Shelf
        ShelfPanel.Bind(_shelfService);
        _shelfService.ShelfChanged += () => Dispatcher.Invoke(UpdateShelfVisibility);

        // Webcam mirror
        if (_settings.ShowWebcam)
        {
            WebcamPanel.Visibility = Visibility.Visible;
            WebcamMirror.Bind(_webcamService);
        }

        // Vertical side panel: wire media + clock
        _mediaService.MediaInfo.PropertyChanged += (_, e) =>
        {
            Dispatcher.Invoke(() => UpdateSideMediaContent(_mediaService.MediaInfo, e.PropertyName));
        };
        _mediaService.SessionChanged += () =>
        {
            Dispatcher.Invoke(() => UpdateSideMediaContent(_mediaService.MediaInfo, null));
        };

        // Side visualizer (skip work while it isn't on screen, e.g. top dock)
        _audioCaptureService.SpectrumUpdated += () =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (SideVisualizer.IsVisible && _audioCaptureService.SpectrumData != null)
                    SideVisualizer.UpdateSpectrum(_audioCaptureService.SpectrumData);
            });
        };

        var sideClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        sideClockTimer.Tick += (_, _) => UpdateSideClock();
        sideClockTimer.Start();
        UpdateSideClock();
        UpdateSideMediaContent(_mediaService.MediaInfo, null);

        // Fullscreen detection — hide this island only when the fullscreen app
        // is on the SAME monitor (a game on screen 1 shouldn't hide screen 2's island)
        _fullscreenService.FullscreenChanged += isFs =>
        {
            Dispatcher.Invoke(() =>
            {
                bool hideThisIsland = isFs &&
                    _fullscreenService.FullscreenMonitor == ScreenHelper.GetMonitorHandle(this);

                NotchCanvas.Opacity = hideThisIsland ? 0 : 1;
                IsHitTestVisible = !hideThisIsland;
            });
        };

        // When HUD shows, expand notch and hide other content
        // Top dock HUD: expand width only (v0.1.0 behavior)
        HudOverlay.HudShown += () => Dispatcher.Invoke(() =>
        {
            if (_vm.NotchState != NotchState.Open)
            {
                LiveActivity.Visibility = Visibility.Collapsed;
                ClockWidget.Visibility = Visibility.Collapsed;
                BatteryIndicator.Suppressed = true;
                _widthSpring.AnimateTo(NotchConstants.HudWidth, _currentWidth);
            }
        });
        HudOverlay.HudDismissed += () => Dispatcher.Invoke(() =>
        {
            UpdateClosedContentVisibility();
            BatteryIndicator.Suppressed = false;
            if (_vm.NotchState != NotchState.Open)
            {
                _widthSpring.AnimateTo(NotchConstants.ClosedWidth, _currentWidth);
            }
        });

        // Side dock HUD: expand height only
        VerticalHudOverlay.HudShown += () => Dispatcher.Invoke(() =>
        {
            if (_vm.NotchState != NotchState.Open)
            {
                SideTimeSmall.Visibility = Visibility.Collapsed;
                _heightSpring.AnimateTo(NotchConstants.HudWidth, _currentHeight);
            }
        });
        VerticalHudOverlay.HudDismissed += () => Dispatcher.Invoke(() =>
        {
            SideTimeSmall.Visibility = Visibility.Visible;
            if (_vm.NotchState != NotchState.Open)
            {
                _heightSpring.AnimateTo(NotchConstants.ClosedWidth, _currentHeight);
            }
        });

        // Apply all persisted settings now that every component is bound —
        // without this, toggles like ShowMusicControls or ShowBattery only
        // take effect after the user touches a setting.
        ApplySettings(_settings);
    }
}
