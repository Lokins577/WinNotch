using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch.Models;
using WinNotch.Plugins;

namespace FocusTimerPlugin;

[WinNotchPlugin("com.winnotch.focustimer", "Focus Timer", "1.0.1", "WinNotch Team")]
public sealed class FocusTimerPlugin : PluginBase, IUIPlugin, IConfigurablePlugin
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public override string Id => "com.winnotch.focustimer";
    public override string Name => "Focus Timer";
    public override string Version => "1.0.1";
    public override string Author => "WinNotch Team";
    public override string Description => "Pomodoro-style focus timer with break cycles, streak tracking, and compact session controls.";
    public override string MinimumWinNotchVersion => "0.5.3";

    private IPluginThemeService? _themeService;
    private DispatcherTimer? _timer;
    private FocusTimerView? _openView;
    private FocusTimerView? _verticalView;
    private FocusTimerSettings _settings = FocusTimerSettings.CreateDefault();
    private FocusTimerState _state = FocusTimerState.CreateDefault();
    private TimerPhase _phase = TimerPhase.Focus;
    private TimeSpan _remaining = TimeSpan.FromMinutes(25);
    private bool _isRunning;
    private string? _settingsDirectory;
    private string? _settingsPath;
    private string? _statePath;
    private DateTime _settingsLastWriteUtc = DateTime.MinValue;
    private bool _isLight;

    public override async Task InitializeAsync(IPluginContext context)
    {
        await base.InitializeAsync(context);

        _themeService = context.Theme;
        _isLight = _themeService.IsLight;
        _themeService.ThemeChanged += OnThemeChanged;

        _settingsDirectory = context.GetPluginDataPath(Id);
        _settingsPath = Path.Combine(_settingsDirectory, "settings.json");
        _statePath = Path.Combine(_settingsDirectory, "state.json");

        await EnsureSettingsFileExistsAsync();
        await LoadSettingsAsync();
        await LoadStateAsync();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        UpdateViews();
    }

    public UIElement? GetUIElement(UIPluginLocation location)
    {
        return location switch
        {
            UIPluginLocation.OpenContent => BuildOpenView(),
            UIPluginLocation.VerticalOpenContent => BuildVerticalView(),
            _ => null
        };
    }

    public void OnNotchStateChanged(NotchState newState)
    {
        if (newState == NotchState.Open)
        {
            _ = ReloadSettingsIfChangedAsync();
            UpdateViews();
        }
    }

    private UIElement BuildOpenView()
    {
        if (_openView != null)
            return _openView.Root;

        _openView = CreateView(width: 332, compact: false);
        UpdateViews();
        return _openView.Root;
    }

    private UIElement BuildVerticalView()
    {
        if (_verticalView != null)
            return _verticalView.Root;

        _verticalView = CreateView(width: 168, compact: true);
        _verticalView.Root.Margin = new Thickness(0, 0, 0, 2);
        UpdateViews();
        return _verticalView.Root;
    }

    private FocusTimerView CreateView(double width, bool compact)
    {
        var root = new Border
        {
            Width = width,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 4, 0, 0),
            BorderThickness = new Thickness(0)
        };

        var outer = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel();

        var titleText = new TextBlock
        {
            Text = "Focus Timer",
            FontSize = compact ? 13 : 14,
            FontWeight = FontWeights.SemiBold
        };
        titleStack.Children.Add(titleText);

        var summaryText = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0)
        };
        titleStack.Children.Add(summaryText);

        Grid.SetColumn(titleStack, 0);
        header.Children.Add(titleStack);

        var configButton = new Button
        {
            Content = "Config",
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 10,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brushes.Transparent
        };
        configButton.Click += (_, _) => OpenConfigurationPanel();
        Grid.SetColumn(configButton, 1);
        header.Children.Add(configButton);

        outer.Children.Add(header);

        var timerText = new TextBlock
        {
            FontSize = compact ? 28 : 34,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 8, 0, 0)
        };
        outer.Children.Add(timerText);

        var phaseText = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        outer.Children.Add(phaseText);

        var separator = CreateSeparator();
        outer.Children.Add(separator);

        var statusRow = CreateInfoRow();
        outer.Children.Add(statusRow.Root);

        var streakRow = CreateInfoRow();
        outer.Children.Add(streakRow.Root);

        var buttonRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var startPauseButton = CreateActionButton("Start");
        startPauseButton.Click += async (_, _) =>
        {
            await ReloadSettingsIfChangedAsync();
            ToggleRunning();
        };
        Grid.SetColumn(startPauseButton, 0);
        buttonRow.Children.Add(startPauseButton);

        var skipButton = CreateActionButton("Skip");
        skipButton.Margin = new Thickness(10, 0, 0, 0);
        skipButton.Click += async (_, _) =>
        {
            await ReloadSettingsIfChangedAsync();
            SkipPhase();
        };
        Grid.SetColumn(skipButton, 1);
        buttonRow.Children.Add(skipButton);

        var resetButton = CreateActionButton("Reset");
        resetButton.Margin = new Thickness(10, 0, 0, 0);
        resetButton.Click += async (_, _) =>
        {
            await ReloadSettingsIfChangedAsync();
            ResetCurrentPhase();
        };
        Grid.SetColumn(resetButton, 2);
        buttonRow.Children.Add(resetButton);

        outer.Children.Add(buttonRow);
        root.Child = outer;

        return new FocusTimerView(
            root,
            titleText,
            summaryText,
            timerText,
            phaseText,
            separator,
            statusRow.Marker,
            statusRow.Label,
            statusRow.Value,
            streakRow.Marker,
            streakRow.Label,
            streakRow.Value,
            configButton,
            startPauseButton,
            skipButton,
            resetButton,
            compact);
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        await ReloadSettingsIfChangedAsync();

        if (!_isRunning)
        {
            UpdateViews();
            return;
        }

        if (_remaining > TimeSpan.Zero)
        {
            _remaining -= TimeSpan.FromSeconds(1);
        }

        if (_remaining <= TimeSpan.Zero)
        {
            AdvancePhase(completedNaturally: true);
        }

        UpdateViews();
    }

    private void ToggleRunning()
    {
        _isRunning = !_isRunning;
        SaveState();
        UpdateViews();
    }

    private void ResetCurrentPhase()
    {
        _remaining = GetPhaseDuration(_phase);
        _isRunning = false;
        SaveState();
        UpdateViews();
    }

    private void SkipPhase()
    {
        AdvancePhase(completedNaturally: false);
        _isRunning = false;
        SaveState();
        UpdateViews();
    }

    private void AdvancePhase(bool completedNaturally)
    {
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        if (!string.Equals(_state.LastActiveDate, today, StringComparison.Ordinal))
        {
            _state.CompletedFocusToday = 0;
            _state.LastActiveDate = today;
        }

        if (_phase == TimerPhase.Focus)
        {
            if (completedNaturally)
            {
                _state.CompletedFocusToday++;
                _state.FocusSessionsSinceLongBreak++;
            }

            _phase = completedNaturally && _state.FocusSessionsSinceLongBreak >= _settings.LongBreakEvery
                ? TimerPhase.LongBreak
                : TimerPhase.ShortBreak;

            if (_phase == TimerPhase.LongBreak && completedNaturally)
            {
                _state.FocusSessionsSinceLongBreak = 0;
            }
        }
        else
        {
            _phase = TimerPhase.Focus;
        }

        _remaining = GetPhaseDuration(_phase);
        SaveState();
    }

    private async Task EnsureSettingsFileExistsAsync()
    {
        if (string.IsNullOrWhiteSpace(_settingsPath) || File.Exists(_settingsPath))
            return;

        var defaultFile = """
{
  "focusMinutes": 25,
  "shortBreakMinutes": 5,
  "longBreakMinutes": 15,
  "longBreakEvery": 4
}
""";

        await File.WriteAllTextAsync(_settingsPath, defaultFile, Encoding.UTF8);
    }

    private async Task LoadSettingsAsync()
    {
        if (string.IsNullOrWhiteSpace(_settingsPath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath);
            var parsed = JsonSerializer.Deserialize<FocusTimerSettings>(json, JsonOptions) ?? FocusTimerSettings.CreateDefault();
            parsed.Normalize();
            _settings = parsed;
            _settingsLastWriteUtc = File.GetLastWriteTimeUtc(_settingsPath);

            if (_remaining > GetPhaseDuration(_phase))
            {
                _remaining = GetPhaseDuration(_phase);
            }
        }
        catch (Exception ex)
        {
            _settings = FocusTimerSettings.CreateDefault();
            Context?.Log($"Focus Timer settings load failed: {ex.Message}", PluginLogLevel.Warning);
        }
    }

    private async Task ReloadSettingsIfChangedAsync()
    {
        if (string.IsNullOrWhiteSpace(_settingsPath) || !File.Exists(_settingsPath))
            return;

        var updatedAt = File.GetLastWriteTimeUtc(_settingsPath);
        if (updatedAt <= _settingsLastWriteUtc)
            return;

        await LoadSettingsAsync();
        UpdateViews();
    }

    private async Task LoadStateAsync()
    {
        _state = FocusTimerState.CreateDefault();

        if (!string.IsNullOrWhiteSpace(_statePath) && File.Exists(_statePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_statePath);
                var parsed = JsonSerializer.Deserialize<FocusTimerState>(json, JsonOptions);
                if (parsed != null)
                {
                    _state = parsed;
                }
            }
            catch (Exception ex)
            {
                Context?.Log($"Focus Timer state load failed: {ex.Message}", PluginLogLevel.Warning);
            }
        }

        _state.Normalize(_settings);
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        if (!string.Equals(_state.LastActiveDate, today, StringComparison.Ordinal))
        {
            _state.CompletedFocusToday = 0;
            _state.LastActiveDate = today;
        }

        _phase = ParsePhase(_state.PhaseName);
        _remaining = TimeSpan.FromSeconds(_state.RemainingSeconds);
        _isRunning = _state.IsRunning;
        SaveState();
    }

    private void SaveState()
    {
        if (string.IsNullOrWhiteSpace(_statePath))
            return;

        _state.LastActiveDate = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        _state.PhaseName = _phase.ToString();
        _state.RemainingSeconds = (int)Math.Max(1, _remaining.TotalSeconds);
        _state.IsRunning = _isRunning;

        try
        {
            var json = JsonSerializer.Serialize(_state, JsonOptions);
            File.WriteAllText(_statePath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Context?.Log($"Focus Timer state save failed: {ex.Message}", PluginLogLevel.Warning);
        }
    }

    private void UpdateViews()
    {
        UpdateView(_openView);
        UpdateView(_verticalView);
    }

    private void UpdateView(FocusTimerView? view)
    {
        if (view == null)
            return;

        view.ConfigButton.Content = "Configure";
        view.ConfigButton.ToolTip = "Open Focus Timer settings in the Plugin Manager.";
        view.SummaryText.Text = $"Pomodoro  {_settings.FocusMinutes}/{_settings.ShortBreakMinutes}/{_settings.LongBreakMinutes}";
        view.TimerText.Text = $"{(int)_remaining.TotalMinutes:00}:{_remaining.Seconds:00}";
        view.PhaseText.Text = GetPhaseLabel();
        view.StatusLabel.Text = "Status";
        view.StatusValue.Text = GetStatusText();
        view.StreakLabel.Text = "Today";
        view.StreakValue.Text = $"{_state.CompletedFocusToday} focus session{(_state.CompletedFocusToday == 1 ? string.Empty : "s")} completed";
        view.StartPauseButton.Content = _isRunning ? "Pause" : "Start";

        ApplyTheme(view);
    }

    private void ApplyTheme(FocusTimerView view)
    {
        view.Root.Background = Brushes.Transparent;
        view.Root.BorderBrush = Brushes.Transparent;
        view.Root.BorderThickness = new Thickness(0);
        view.TitleText.Foreground = GetPrimaryForeground();
        view.SummaryText.Foreground = GetSecondaryForeground();
        view.TimerText.Foreground = GetPrimaryForeground();
        view.PhaseText.Foreground = GetSecondaryForeground();
        view.Separator.Background = GetSeparatorBrush();
        view.StatusMarker.Background = GetAccentBrush();
        view.StatusLabel.Foreground = GetSecondaryForeground();
        view.StatusValue.Foreground = GetPrimaryForeground();
        view.StreakMarker.Background = GetSeparatorBrush();
        view.StreakLabel.Foreground = GetSecondaryForeground();
        view.StreakValue.Foreground = GetPrimaryForeground();

        ApplyButtonTheme(view.ConfigButton, secondary: true);
        ApplyButtonTheme(view.StartPauseButton, secondary: false);
        ApplyButtonTheme(view.SkipButton, secondary: true);
        ApplyButtonTheme(view.ResetButton, secondary: true);
    }

    private void ApplyButtonTheme(Button button, bool secondary)
    {
        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.Foreground = secondary ? GetSecondaryForeground() : GetAccentBrush();
        button.FontWeight = secondary ? FontWeights.Medium : FontWeights.SemiBold;
    }

    private void OnThemeChanged()
    {
        _isLight = _themeService?.IsLight ?? false;
        UpdateViews();
    }

    private void OpenConfigurationPanel()
    {
        OpenPluginManagerConfiguration();
    }

    public PluginConfigurationDefinition GetConfigurationDefinition()
    {
        return new PluginConfigurationDefinition
        {
            Title = "Focus Timer",
            Description = "Tune the lengths of your focus and break sessions without leaving the plugin manager.",
            StatusText = "Optional settings. Focus Timer already works with the classic 25/5/15 Pomodoro defaults.",
            RequiresConfiguration = false,
            IsConfigured = true,
            Sections =
            [
                new PluginConfigurationSection
                {
                    Title = "Session lengths",
                    Description = "Choose how long focus and break sessions should last.",
                    Fields =
                    [
                        new PluginConfigurationField
                        {
                            Key = "focusMinutes",
                            Label = "Focus minutes",
                            HelpText = "How long each work session should last before a break begins.",
                            Value = _settings.FocusMinutes.ToString(),
                            Placeholder = "Between 1 and 90 minutes.",
                            FieldType = PluginConfigurationFieldType.Number,
                            IsRequired = true
                        },
                        new PluginConfigurationField
                        {
                            Key = "shortBreakMinutes",
                            Label = "Short break minutes",
                            HelpText = "The quick reset between most focus sessions.",
                            Value = _settings.ShortBreakMinutes.ToString(),
                            Placeholder = "Between 1 and 30 minutes.",
                            FieldType = PluginConfigurationFieldType.Number,
                            IsRequired = true
                        },
                        new PluginConfigurationField
                        {
                            Key = "longBreakMinutes",
                            Label = "Long break minutes",
                            HelpText = "The deeper break that arrives after several focus sessions.",
                            Value = _settings.LongBreakMinutes.ToString(),
                            Placeholder = "Between 5 and 45 minutes.",
                            FieldType = PluginConfigurationFieldType.Number,
                            IsRequired = true
                        },
                        new PluginConfigurationField
                        {
                            Key = "longBreakEvery",
                            Label = "Long break every",
                            HelpText = "How many focus sessions should complete before a long break is scheduled.",
                            Value = _settings.LongBreakEvery.ToString(),
                            Placeholder = "Between 2 and 8 focus sessions.",
                            FieldType = PluginConfigurationFieldType.Number,
                            IsRequired = true
                        }
                    ]
                }
            ]
        };
    }

    public async Task<PluginConfigurationResult> ApplyConfigurationAsync(IReadOnlyDictionary<string, string?> values)
    {
        if (!TryReadNumber(values, "focusMinutes", out var focusMinutes) ||
            !TryReadNumber(values, "shortBreakMinutes", out var shortBreakMinutes) ||
            !TryReadNumber(values, "longBreakMinutes", out var longBreakMinutes) ||
            !TryReadNumber(values, "longBreakEvery", out var longBreakEvery))
        {
            return PluginConfigurationResult.Fail("All Focus Timer values must be whole numbers.");
        }

        var settings = new FocusTimerSettings
        {
            FocusMinutes = focusMinutes,
            ShortBreakMinutes = shortBreakMinutes,
            LongBreakMinutes = longBreakMinutes,
            LongBreakEvery = longBreakEvery
        };
        settings.Normalize();

        await SaveSettingsAsync(settings);
        return PluginConfigurationResult.Ok("Focus Timer settings saved.");
    }

    private async Task SaveSettingsAsync(FocusTimerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(_settingsPath))
            throw new InvalidOperationException("Focus Timer settings path is not available.");

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json, Encoding.UTF8);

        _settings = settings;
        _settingsLastWriteUtc = File.GetLastWriteTimeUtc(_settingsPath);
        _state.Normalize(_settings);
        _phase = ParsePhase(_state.PhaseName);
        _remaining = TimeSpan.FromSeconds(Math.Clamp(_state.RemainingSeconds, 1, (int)GetPhaseDuration(_phase).TotalSeconds));
        UpdateViews();
        SaveState();
    }

    private static bool TryReadNumber(IReadOnlyDictionary<string, string?> values, string key, out int value)
    {
        value = 0;
        return values.TryGetValue(key, out var rawValue) && int.TryParse(rawValue, out value);
    }

    private string GetPhaseLabel()
    {
        return _phase switch
        {
            TimerPhase.ShortBreak => "Short break running",
            TimerPhase.LongBreak => "Long break running",
            _ => "Focus session running"
        };
    }

    private string GetStatusText()
    {
        if (_phase == TimerPhase.Focus)
        {
            var untilLongBreak = Math.Max(0, _settings.LongBreakEvery - _state.FocusSessionsSinceLongBreak);
            return _isRunning
                ? $"Stay on task. Long break in {untilLongBreak} focus block{(untilLongBreak == 1 ? string.Empty : "s")}."
                : "Ready for your next focus sprint.";
        }

        return _isRunning
            ? "Take a breath, stretch, and come back fresh."
            : "Break is queued. Resume when you are ready.";
    }

    private TimeSpan GetPhaseDuration(TimerPhase phase)
    {
        return phase switch
        {
            TimerPhase.ShortBreak => TimeSpan.FromMinutes(_settings.ShortBreakMinutes),
            TimerPhase.LongBreak => TimeSpan.FromMinutes(_settings.LongBreakMinutes),
            _ => TimeSpan.FromMinutes(_settings.FocusMinutes)
        };
    }

    private static TimerPhase ParsePhase(string? phaseName)
    {
        return Enum.TryParse<TimerPhase>(phaseName, true, out var phase)
            ? phase
            : TimerPhase.Focus;
    }

    private static Border CreateSeparator()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(0, 8, 0, 8)
        };
    }

    private static (Grid Root, Border Marker, TextBlock Label, TextBlock Value) CreateInfoRow()
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var marker = new Border
        {
            Width = 2,
            Margin = new Thickness(0, 1, 10, 1)
        };
        row.Children.Add(marker);

        var stack = new StackPanel();
        var label = new TextBlock { FontSize = 10 };
        var value = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(label);
        stack.Children.Add(value);
        Grid.SetColumn(stack, 1);
        row.Children.Add(stack);

        return (row, marker, label, value);
    }

    private static Button CreateActionButton(string label)
    {
        return new Button
        {
            Content = label,
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 10,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brushes.Transparent
        };
    }

    private Brush GetAccentBrush()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(53, 116, 219)
            : Color.FromRgb(78, 148, 245));
    }

    private Brush GetSeparatorBrush()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(210, 216, 226)
            : Color.FromRgb(82, 88, 98));
    }

    private Brush GetPrimaryForeground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(30, 35, 43)
            : Color.FromRgb(241, 244, 249));
    }

    private Brush GetSecondaryForeground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(96, 103, 114)
            : Color.FromRgb(181, 187, 196));
    }

    public override Task ShutdownAsync()
    {
        if (_themeService != null)
            _themeService.ThemeChanged -= OnThemeChanged;

        if (_timer != null)
            _timer.Tick -= OnTimerTick;

        _timer?.Stop();
        _timer = null;
        SaveState();

        return base.ShutdownAsync();
    }

    public override void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        SaveState();
        base.Dispose();
    }

    private sealed record FocusTimerView(
        Border Root,
        TextBlock TitleText,
        TextBlock SummaryText,
        TextBlock TimerText,
        TextBlock PhaseText,
        Border Separator,
        Border StatusMarker,
        TextBlock StatusLabel,
        TextBlock StatusValue,
        Border StreakMarker,
        TextBlock StreakLabel,
        TextBlock StreakValue,
        Button ConfigButton,
        Button StartPauseButton,
        Button SkipButton,
        Button ResetButton,
        bool Compact);

    private enum TimerPhase
    {
        Focus,
        ShortBreak,
        LongBreak
    }

    private sealed class FocusTimerSettings
    {
        [JsonPropertyName("focusMinutes")]
        public int FocusMinutes { get; set; } = 25;

        [JsonPropertyName("shortBreakMinutes")]
        public int ShortBreakMinutes { get; set; } = 5;

        [JsonPropertyName("longBreakMinutes")]
        public int LongBreakMinutes { get; set; } = 15;

        [JsonPropertyName("longBreakEvery")]
        public int LongBreakEvery { get; set; } = 4;

        public void Normalize()
        {
            FocusMinutes = Math.Clamp(FocusMinutes, 1, 90);
            ShortBreakMinutes = Math.Clamp(ShortBreakMinutes, 1, 30);
            LongBreakMinutes = Math.Clamp(LongBreakMinutes, 5, 45);
            LongBreakEvery = Math.Clamp(LongBreakEvery, 2, 8);
        }

        public static FocusTimerSettings CreateDefault()
        {
            var settings = new FocusTimerSettings();
            settings.Normalize();
            return settings;
        }
    }

    private sealed class FocusTimerState
    {
        [JsonPropertyName("lastActiveDate")]
        public string LastActiveDate { get; set; } = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");

        [JsonPropertyName("completedFocusToday")]
        public int CompletedFocusToday { get; set; }

        [JsonPropertyName("focusSessionsSinceLongBreak")]
        public int FocusSessionsSinceLongBreak { get; set; }

        [JsonPropertyName("phase")]
        public string PhaseName { get; set; } = "Focus";

        [JsonPropertyName("remainingSeconds")]
        public int RemainingSeconds { get; set; } = 25 * 60;

        [JsonPropertyName("isRunning")]
        public bool IsRunning { get; set; }

        public void Normalize(FocusTimerSettings settings)
        {
            CompletedFocusToday = Math.Max(0, CompletedFocusToday);
            FocusSessionsSinceLongBreak = Math.Max(0, FocusSessionsSinceLongBreak);
            var phase = ParsePhase(PhaseName);
            var maxSeconds = (int)Math.Max(60, GetDefaultDurationSeconds(phase, settings));
            RemainingSeconds = Math.Clamp(RemainingSeconds, 1, maxSeconds);
            PhaseName = phase.ToString();
        }

        private static int GetDefaultDurationSeconds(TimerPhase phase, FocusTimerSettings settings)
        {
            return phase switch
            {
                TimerPhase.ShortBreak => settings.ShortBreakMinutes * 60,
                TimerPhase.LongBreak => settings.LongBreakMinutes * 60,
                _ => settings.FocusMinutes * 60
            };
        }

        public static FocusTimerState CreateDefault()
        {
            return new FocusTimerState();
        }
    }
}
