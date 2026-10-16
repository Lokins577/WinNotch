using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinNotch.Models;
using WinNotch.Plugins;

namespace PetWidgetPlugin;

[WinNotchPlugin("com.winnotch.petwidget", "Pet Widget", "1.0.1", "WinNotch Team")]
public class PetWidgetPlugin : PluginBase, IUIPlugin
{
    public override string Id => "com.winnotch.petwidget";
    public override string Name => "Pet Widget";
    public override string Version => "1.0.1";
    public override string Author => "WinNotch Team";
    public override string Description => "A tiny notch cat that reacts to battery level, mood, and attention.";
    public override string MinimumWinNotchVersion => "0.3.0";

    private IPluginThemeService? _themeService;
    private IPluginBatteryService? _batteryService;

    private TextBlock? _closedEmoji;
    private Border? _openContainer;
    private TextBlock? _moodLabel;
    private TextBlock? _happinessLabel;
    private TextBlock? _statusText;
    private ProgressBar? _happinessBar;
    private Button? _petButton;
    private Canvas? _petCanvas;
    private Line? _floorLine;
    private TextBlock? _petSprite;

    private DispatcherTimer? _animTimer;
    private DispatcherTimer? _moodTimer;

    private readonly Random _rng = new();
    private PetMood _mood = PetMood.Happy;
    private PetAction _action = PetAction.Idle;
    private int _happiness = 78;
    private double _petX = 18;
    private double _arenaWidth = 120;
    private int _direction = 1;
    private int _idleTicks;
    private int _sleepTicks;
    private int _frame;
    private bool _isLight;
    private DateTime _lastPetTime = DateTime.MinValue;

    public override async Task InitializeAsync(IPluginContext context)
    {
        await base.InitializeAsync(context);

        _themeService = context.Theme;
        _batteryService = context.Battery;
        _isLight = _themeService.IsLight;
        _themeService.ThemeChanged += OnThemeChanged;

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _animTimer.Tick += OnAnimTick;
        _animTimer.Start();

        _moodTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _moodTimer.Tick += OnMoodTick;
        _moodTimer.Start();

        PickNextAction();
    }

    public UIElement? GetUIElement(UIPluginLocation location)
    {
        return location switch
        {
            UIPluginLocation.ClosedContent => BuildClosedUi(),
            UIPluginLocation.OpenContent => BuildOpenUi(),
            _ => null
        };
    }

    public void OnNotchStateChanged(NotchState newState)
    {
        if (newState == NotchState.Open)
        {
            UpdateOpenUi();
        }
    }

    private UIElement BuildClosedUi()
    {
        _closedEmoji = new TextBlock
        {
            FontSize = 14,
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        UpdateClosedUi();
        return _closedEmoji;
    }

    private UIElement BuildOpenUi()
    {
        _openContainer = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 4, 0, 0),
            MinHeight = 112,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var root = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _moodLabel = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_moodLabel, 0);
        header.Children.Add(_moodLabel);

        _petButton = new Button
        {
            Content = "Pet",
            FontSize = 11,
            Padding = new Thickness(8, 3, 8, 3),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _petButton.Click += OnPetClicked;
        Grid.SetColumn(_petButton, 1);
        header.Children.Add(_petButton);

        root.Children.Add(header);

        _petCanvas = new Canvas
        {
            Height = 48,
            ClipToBounds = true,
            Margin = new Thickness(0, 8, 0, 6)
        };
        _petCanvas.SizeChanged += OnPetCanvasSizeChanged;

        _floorLine = new Line
        {
            X1 = 0,
            Y1 = 44,
            Y2 = 44,
            StrokeThickness = 1
        };
        _petCanvas.Children.Add(_floorLine);

        _petSprite = new TextBlock
        {
            FontSize = 24,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        _petCanvas.Children.Add(_petSprite);

        root.Children.Add(_petCanvas);

        _happinessLabel = new TextBlock
        {
            Text = "Happiness",
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        };
        root.Children.Add(_happinessLabel);

        _happinessBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 6
        };
        root.Children.Add(_happinessBar);

        _statusText = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(_statusText);

        _openContainer.Child = root;

        ApplyTheme();
        UpdateOpenUi();
        return _openContainer;
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        _frame++;

        switch (_action)
        {
            case PetAction.Walking:
                MovePet(3.2);
                if (_petX <= 4 || _petX >= GetRightBoundary())
                {
                    _direction *= -1;
                    PickNextAction();
                }
                break;

            case PetAction.Playing:
                MovePet(4.6);
                if (_petX <= 4 || _petX >= GetRightBoundary())
                {
                    _direction *= -1;
                }

                _idleTicks++;
                if (_idleTicks > 18)
                {
                    PickNextAction();
                }
                break;

            case PetAction.Sleeping:
                _sleepTicks++;
                if (_sleepTicks > 42)
                {
                    _action = PetAction.Idle;
                    _idleTicks = 0;
                    _sleepTicks = 0;
                }
                break;

            case PetAction.Idle:
                _idleTicks++;
                if (_idleTicks > 24)
                {
                    PickNextAction();
                }
                break;
        }

        UpdatePetVisual();
        UpdateClosedUi();

        if (_statusText != null)
        {
            _statusText.Text = GetStatusText();
        }
    }

    private void OnMoodTick(object? sender, EventArgs e)
    {
        _happiness = Math.Max(0, _happiness - 2);

        if (IsBatteryLow())
        {
            _happiness = Math.Max(0, _happiness - 4);
        }

        UpdateMoodFromHappiness();

        if (_happiness < 25 && _action == PetAction.Playing)
        {
            PickNextAction();
        }

        UpdateOpenUi();
        UpdateClosedUi();
    }

    private void OnPetClicked(object sender, RoutedEventArgs e)
    {
        if ((DateTime.Now - _lastPetTime).TotalSeconds < 3)
        {
            return;
        }

        _lastPetTime = DateTime.Now;
        _happiness = Math.Min(100, _happiness + 15);
        _direction = _rng.Next(2) == 0 ? 1 : -1;
        _action = PetAction.Playing;
        _idleTicks = 0;
        _sleepTicks = 0;

        UpdateMoodFromHappiness();
        UpdateOpenUi();
        UpdateClosedUi();
    }

    private void OnThemeChanged()
    {
        _isLight = _themeService?.IsLight ?? false;
        ApplyTheme();
    }

    private void OnPetCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _arenaWidth = Math.Max(72, e.NewSize.Width);

        if (_floorLine != null)
        {
            _floorLine.X2 = Math.Max(0, _arenaWidth);
        }

        _petX = Math.Clamp(_petX, 4, GetRightBoundary());
        UpdatePetVisual();
    }

    private void PickNextAction()
    {
        _idleTicks = 0;
        _sleepTicks = 0;

        if (IsBatteryLow() || _happiness < 20)
        {
            _action = _rng.Next(3) == 0 ? PetAction.Sleeping : PetAction.Idle;
            return;
        }

        if (_happiness >= 80)
        {
            var roll = _rng.Next(100);
            _action = roll switch
            {
                < 35 => PetAction.Walking,
                < 70 => PetAction.Playing,
                _ => PetAction.Idle
            };
        }
        else if (_happiness >= 45)
        {
            _action = _rng.Next(2) == 0 ? PetAction.Walking : PetAction.Idle;
        }
        else
        {
            _action = PetAction.Idle;
        }

        if (_action is PetAction.Walking or PetAction.Playing)
        {
            _direction = _rng.Next(2) == 0 ? 1 : -1;
        }
    }

    private void MovePet(double amount)
    {
        _petX = Math.Clamp(_petX + amount * _direction, 4, GetRightBoundary());
    }

    private double GetRightBoundary()
    {
        return Math.Max(4, _arenaWidth - 28);
    }

    private void UpdateMoodFromHappiness()
    {
        _mood = _happiness switch
        {
            >= 85 => PetMood.Ecstatic,
            >= 60 => PetMood.Happy,
            >= 40 => PetMood.Content,
            >= 20 => PetMood.Sad,
            _ => PetMood.Sleepy
        };
    }

    private void UpdateClosedUi()
    {
        if (_closedEmoji == null)
        {
            return;
        }

        _closedEmoji.Text = GetMoodEmoji();
        _closedEmoji.ToolTip = $"Pet mood: {_mood}";
    }

    private void UpdateOpenUi()
    {
        if (_moodLabel != null)
        {
            _moodLabel.Text = $"{GetMoodEmoji()} {_mood}";
        }

        if (_happinessBar != null)
        {
            _happinessBar.Value = _happiness;
            _happinessBar.Foreground = GetHappinessBrush();
        }

        if (_statusText != null)
        {
            _statusText.Text = GetStatusText();
        }

        UpdatePetVisual();
        ApplyTheme();
    }

    private void UpdatePetVisual()
    {
        if (_petSprite == null)
        {
            return;
        }

        var bounce = _action switch
        {
            PetAction.Walking => _frame % 2 == 0 ? 0 : -2,
            PetAction.Playing => _frame % 4 < 2 ? -4 : -1,
            PetAction.Sleeping => 1,
            _ => 0
        };

        _petSprite.Text = GetPetSprite();
        _petSprite.RenderTransform = _direction < 0
            ? new ScaleTransform(-1, 1)
            : Transform.Identity;

        Canvas.SetLeft(_petSprite, _petX);
        Canvas.SetTop(_petSprite, 10 + bounce);
    }

    private void ApplyTheme()
    {
        if (_openContainer != null)
        {
            _openContainer.Background = GetCardBackground();
        }

        if (_moodLabel != null)
        {
            _moodLabel.Foreground = GetPrimaryForeground();
        }

        if (_statusText != null)
        {
            _statusText.Foreground = GetSecondaryForeground();
        }

        if (_happinessLabel != null)
        {
            _happinessLabel.Foreground = GetSecondaryForeground();
        }

        if (_petButton != null)
        {
            _petButton.Background = GetButtonBackground();
            _petButton.Foreground = GetPrimaryForeground();
        }

        if (_happinessBar != null)
        {
            _happinessBar.Background = GetTrackBrush();
            _happinessBar.Foreground = GetHappinessBrush();
        }

        if (_floorLine != null)
        {
            _floorLine.Stroke = GetFloorBrush();
        }
    }

    private bool IsBatteryLow()
    {
        if (_batteryService == null)
        {
            return false;
        }

        try
        {
            return _batteryService.ChargePercent is >= 0 and < 15;
        }
        catch
        {
            return false;
        }
    }

    private string GetPetSprite()
    {
        return _action switch
        {
            PetAction.Sleeping => "😴",
            PetAction.Playing => _frame % 4 < 2 ? "😸" : "😺",
            PetAction.Walking => _frame % 2 == 0 ? "🐱" : "😺",
            _ => _mood switch
            {
                PetMood.Ecstatic => "😸",
                PetMood.Happy => "😺",
                PetMood.Content => "🐱",
                PetMood.Sad => "😿",
                PetMood.Sleepy => "😴",
                _ => "🐱"
            }
        };
    }

    private string GetMoodEmoji()
    {
        return _mood switch
        {
            PetMood.Ecstatic => "😸",
            PetMood.Happy => "😺",
            PetMood.Content => "🐱",
            PetMood.Sad => "😿",
            PetMood.Sleepy => "😴",
            _ => "🐱"
        };
    }

    private string GetStatusText()
    {
        if (IsBatteryLow())
        {
            return "Battery is low, so your cat is curling up nearby.";
        }

        return _action switch
        {
            PetAction.Walking => "Padding around the notch.",
            PetAction.Playing => "Full zoomies mode.",
            PetAction.Sleeping => "Sleeping through the notifications.",
            _ => _mood switch
            {
                PetMood.Ecstatic => "Purring loudly and claiming the notch.",
                PetMood.Happy => "Very content right now.",
                PetMood.Content => "Watching everything quietly.",
                PetMood.Sad => "Would appreciate a quick pet.",
                PetMood.Sleepy => "Ready for a nap.",
                _ => "Hanging out."
            }
        };
    }

    private Brush GetCardBackground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromArgb(190, 245, 246, 250)
            : Color.FromArgb(190, 38, 40, 46));
    }

    private Brush GetButtonBackground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(228, 231, 238)
            : Color.FromRgb(68, 72, 80));
    }

    private Brush GetTrackBrush()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(222, 225, 232)
            : Color.FromRgb(54, 57, 64));
    }

    private Brush GetFloorBrush()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(198, 201, 208)
            : Color.FromRgb(88, 92, 100));
    }

    private Brush GetPrimaryForeground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(32, 35, 41)
            : Color.FromRgb(241, 244, 250));
    }

    private Brush GetSecondaryForeground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(96, 102, 112)
            : Color.FromRgb(175, 181, 191));
    }

    private Brush GetHappinessBrush()
    {
        if (_happiness >= 60)
        {
            return new SolidColorBrush(Color.FromRgb(86, 194, 118));
        }

        if (_happiness >= 30)
        {
            return new SolidColorBrush(Color.FromRgb(230, 178, 64));
        }

        return new SolidColorBrush(Color.FromRgb(223, 90, 90));
    }

    public override Task ShutdownAsync()
    {
        _animTimer?.Stop();
        _moodTimer?.Stop();

        if (_themeService != null)
        {
            _themeService.ThemeChanged -= OnThemeChanged;
        }

        return base.ShutdownAsync();
    }

    public override void Dispose()
    {
        _animTimer?.Stop();
        _moodTimer?.Stop();
        _animTimer = null;
        _moodTimer = null;
        base.Dispose();
    }
}

internal enum PetMood
{
    Ecstatic,
    Happy,
    Content,
    Sad,
    Sleepy
}

internal enum PetAction
{
    Idle,
    Walking,
    Playing,
    Sleeping
}
