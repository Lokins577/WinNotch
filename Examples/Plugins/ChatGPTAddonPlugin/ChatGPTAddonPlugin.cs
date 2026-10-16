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
using WinNotch.Models;
using WinNotch.Plugins;

namespace ChatGPTAddonPlugin;

[WinNotchPlugin("com.winnotch.chatgpt", "ChatGPT Add-on", "1.0.2", "WinNotch Team")]
[PluginPermission("clipboard", "Required to copy prompts before opening ChatGPT.")]
public sealed class ChatGPTAddonPlugin : PluginBase, IUIPlugin, IConfigurablePlugin
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public override string Id => "com.winnotch.chatgpt";
    public override string Name => "ChatGPT Add-on";
    public override string Version => "1.0.2";
    public override string Author => "WinNotch Team";
    public override string Description => "Quick-launch ChatGPT from the notch with reusable prompt presets and a shared prompt scratchpad.";
    public override string MinimumWinNotchVersion => "0.5.3";

    private IPluginThemeService? _themeService;
    private ChatView? _openView;
    private ChatView? _verticalView;
    private ChatAddonSettings _settings = ChatAddonSettings.CreateDefault();
    private string? _settingsDirectory;
    private string? _settingsPath;
    private DateTime _settingsLastWriteUtc = DateTime.MinValue;
    private string _currentPrompt = string.Empty;
    private string _statusText = "Prompt launcher ready.";
    private bool _isLight;
    private bool _isSyncingText;

    public override async Task InitializeAsync(IPluginContext context)
    {
        await base.InitializeAsync(context);

        _themeService = context.Theme;
        _isLight = _themeService.IsLight;
        _themeService.ThemeChanged += OnThemeChanged;

        _settingsDirectory = context.GetPluginDataPath(Id);
        _settingsPath = Path.Combine(_settingsDirectory, "settings.json");

        await EnsureSettingsFileExistsAsync();
        await LoadSettingsAsync();
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
        }
    }

    private UIElement BuildOpenView()
    {
        if (_openView != null)
            return _openView.Root;

        _openView = CreateView(width: 348, compact: false);
        UpdateView(_openView);
        return _openView.Root;
    }

    private UIElement BuildVerticalView()
    {
        if (_verticalView != null)
            return _verticalView.Root;

        _verticalView = CreateView(width: 178, compact: true);
        _verticalView.Root.Margin = new Thickness(0, 0, 0, 2);
        UpdateView(_verticalView);
        return _verticalView.Root;
    }

    private ChatView CreateView(double width, bool compact)
    {
        var root = new Border
        {
            Width = width,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 4, 0, 0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };

        var outer = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel();

        var titleText = new TextBlock
        {
            Text = "ChatGPT",
            FontSize = compact ? 13 : 14,
            FontWeight = FontWeights.SemiBold
        };
        titleStack.Children.Add(titleText);

        var summaryText = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        titleStack.Children.Add(summaryText);

        Grid.SetColumn(titleStack, 0);
        header.Children.Add(titleStack);

        var configButton = CreateActionButton("Config");
        configButton.Click += (_, _) => OpenConfigurationPanel();
        Grid.SetColumn(configButton, 1);
        header.Children.Add(configButton);

        outer.Children.Add(header);

        var promptBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = compact ? 58 : 78,
            MaxHeight = compact ? 76 : 110,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(10, 8, 10, 8),
            FontSize = compact ? 11 : 12,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0)
        };
        promptBox.TextChanged += (_, _) => OnPromptTextChanged(promptBox);
        outer.Children.Add(promptBox);

        var presetPanel = new WrapPanel
        {
            Margin = new Thickness(0, 8, 0, 0)
        };
        outer.Children.Add(presetPanel);

        var buttonRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copyButton = CreateActionButton("Copy");
        copyButton.Click += (_, _) => CopyPrompt();
        Grid.SetColumn(copyButton, 0);
        buttonRow.Children.Add(copyButton);

        var openButton = CreateActionButton("Open");
        openButton.Margin = new Thickness(10, 0, 0, 0);
        openButton.Click += (_, _) => OpenChatGpt();
        Grid.SetColumn(openButton, 1);
        buttonRow.Children.Add(openButton);

        var clearButton = CreateActionButton("Clear");
        clearButton.Margin = new Thickness(10, 0, 0, 0);
        clearButton.Click += (_, _) => ClearPrompt();
        Grid.SetColumn(clearButton, 2);
        buttonRow.Children.Add(clearButton);

        outer.Children.Add(buttonRow);

        var statusText = new TextBlock
        {
            FontSize = 9,
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        outer.Children.Add(statusText);

        root.Child = outer;

        return new ChatView(
            root,
            titleText,
            summaryText,
            promptBox,
            presetPanel,
            statusText,
            configButton,
            copyButton,
            openButton,
            clearButton,
            compact);
    }

    private void OnPromptTextChanged(TextBox promptBox)
    {
        if (_isSyncingText)
            return;

        _currentPrompt = promptBox.Text;
        UpdateViews(promptBox);
    }

    private void UpdateViews(TextBox? sourceTextBox = null)
    {
        UpdateView(_openView, sourceTextBox);
        UpdateView(_verticalView, sourceTextBox);
    }

    private void UpdateView(ChatView? view, TextBox? sourceTextBox = null)
    {
        if (view == null)
            return;

        view.SummaryText.Text = "Prompt helper for quick launches";
        view.StatusText.Text = _statusText;
        view.ConfigButton.Content = "Configure";
        view.ConfigButton.ToolTip = "Open ChatGPT Add-on settings in the Plugin Manager.";

        if (!ReferenceEquals(view.PromptBox, sourceTextBox) && view.PromptBox.Text != _currentPrompt)
        {
            _isSyncingText = true;
            view.PromptBox.Text = _currentPrompt;
            _isSyncingText = false;
        }

        view.PresetPanel.Children.Clear();
        var maxPresets = view.Compact ? 2 : Math.Min(4, _settings.QuickPrompts.Count);
        for (var index = 0; index < maxPresets; index++)
        {
            view.PresetPanel.Children.Add(CreatePresetButton(_settings.QuickPrompts[index]));
        }

        ApplyTheme(view);
    }

    private Button CreatePresetButton(string prompt)
    {
        var button = new Button
        {
            Content = prompt,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 8),
            FontSize = 10,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += (_, _) =>
        {
            _currentPrompt = prompt;
            _statusText = "Preset loaded.";
            UpdateViews();
        };

        button.Background = GetChipBrush();
        button.Foreground = GetSecondaryForeground();
        return button;
    }

    private void CopyPrompt()
    {
        var text = _currentPrompt.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _statusText = "Write a prompt before copying it.";
            UpdateViews();
            return;
        }

        try
        {
            Clipboard.SetText(text);
            _statusText = "Prompt copied to clipboard.";
        }
        catch (Exception ex)
        {
            _statusText = "Clipboard unavailable.";
            Context?.Log($"ChatGPT Add-on clipboard copy failed: {ex.Message}", PluginLogLevel.Warning);
        }

        UpdateViews();
    }

    private void OpenChatGpt()
    {
        var text = _currentPrompt.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                Clipboard.SetText(text);
                _statusText = "Prompt copied. Opening ChatGPT.";
            }
            catch (Exception ex)
            {
                _statusText = "Opening ChatGPT without copying prompt.";
                Context?.Log($"ChatGPT Add-on clipboard copy failed: {ex.Message}", PluginLogLevel.Warning);
            }
        }
        else
        {
            _statusText = "Opening ChatGPT.";
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _settings.ChatUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _statusText = "Failed to open ChatGPT.";
            Context?.Log($"ChatGPT Add-on open failed: {ex.Message}", PluginLogLevel.Warning);
        }

        UpdateViews();
    }

    private void ClearPrompt()
    {
        _currentPrompt = string.Empty;
        _statusText = "Prompt cleared.";
        UpdateViews();
    }

    private void ApplyTheme(ChatView view)
    {
        view.Root.Background = Brushes.Transparent;
        view.Root.BorderBrush = Brushes.Transparent;
        view.Root.BorderThickness = new Thickness(0);
        view.TitleText.Foreground = GetPrimaryForeground();
        view.SummaryText.Foreground = GetSecondaryForeground();
        view.StatusText.Foreground = GetMutedForeground();

        view.PromptBox.Foreground = GetPrimaryForeground();
        view.PromptBox.Background = GetInputBrush();
        view.PromptBox.CaretBrush = GetPrimaryForeground();

        ApplyButtonTheme(view.ConfigButton, secondary: true);
        ApplyButtonTheme(view.CopyButton, secondary: true);
        ApplyButtonTheme(view.OpenButton, secondary: false);
        ApplyButtonTheme(view.ClearButton, secondary: true);
    }

    private void ApplyButtonTheme(Button button, bool secondary)
    {
        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.Foreground = secondary ? GetSecondaryForeground() : GetAccentBrush();
        button.FontWeight = secondary ? FontWeights.Medium : FontWeights.SemiBold;
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

    private async Task EnsureSettingsFileExistsAsync()
    {
        if (string.IsNullOrWhiteSpace(_settingsPath) || File.Exists(_settingsPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

        var defaultSettings = """
{
  "chatUrl": "https://chatgpt.com/",
  "quickPrompts": [
    "Summarize this clearly.",
    "Rewrite this to sound more polished.",
    "Give me three creative directions for this.",
    "Turn this into an actionable checklist."
  ]
}
""";

        await File.WriteAllTextAsync(_settingsPath, defaultSettings, Encoding.UTF8);
    }

    private async Task LoadSettingsAsync()
    {
        if (string.IsNullOrWhiteSpace(_settingsPath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath);
            var parsed = JsonSerializer.Deserialize<ChatAddonSettings>(json, JsonOptions) ?? ChatAddonSettings.CreateDefault();
            parsed.Normalize();
            _settings = parsed;
            _settingsLastWriteUtc = File.GetLastWriteTimeUtc(_settingsPath);
        }
        catch (Exception ex)
        {
            _settings = ChatAddonSettings.CreateDefault();
            Context?.Log($"ChatGPT Add-on settings load failed: {ex.Message}", PluginLogLevel.Warning);
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

    private void OpenConfigurationPanel()
    {
        OpenPluginManagerConfiguration();
    }

    public PluginConfigurationDefinition GetConfigurationDefinition()
    {
        return new PluginConfigurationDefinition
        {
            Title = "ChatGPT Add-on",
            Description = "Tweak the launch URL and the quick prompt chips that appear inside the notch.",
            StatusText = "Optional settings. The default configuration already works out of the box.",
            RequiresConfiguration = false,
            IsConfigured = true,
            Sections =
            [
                new PluginConfigurationSection
                {
                    Title = "Launch",
                    Description = "Control where the Open action sends you when launching ChatGPT.",
                    Fields =
                    [
                        new PluginConfigurationField
                        {
                            Key = "chatUrl",
                            Label = "Chat URL",
                            HelpText = "Use the full URL you want the Open button to launch. The default goes to chatgpt.com.",
                            Value = _settings.ChatUrl,
                            Placeholder = "Example: https://chatgpt.com/"
                        }
                    ]
                },
                new PluginConfigurationSection
                {
                    Title = "Quick prompts",
                    Description = "Enter one prompt per line. The first prompts appear as chips in the widget.",
                    Fields =
                    [
                        new PluginConfigurationField
                        {
                            Key = "quickPrompts",
                            Label = "Prompt list",
                            HelpText = "Add up to six prompts. Blank lines are ignored automatically.",
                            Value = string.Join(Environment.NewLine, _settings.QuickPrompts),
                            Placeholder = "One prompt per line.",
                            FieldType = PluginConfigurationFieldType.Multiline
                        }
                    ]
                }
            ]
        };
    }

    public async Task<PluginConfigurationResult> ApplyConfigurationAsync(IReadOnlyDictionary<string, string?> values)
    {
        var settings = ChatAddonSettings.CreateDefault();
        settings.ChatUrl = ReadValue(values, "chatUrl", _settings.ChatUrl).Trim();
        settings.QuickPrompts = ReadMultilineValues(values, "quickPrompts", _settings.QuickPrompts);
        settings.Normalize();

        if (!Uri.TryCreate(settings.ChatUrl, UriKind.Absolute, out _))
            return PluginConfigurationResult.Fail("Chat URL must be a valid absolute URL.");

        await SaveSettingsAsync(settings);
        return PluginConfigurationResult.Ok("ChatGPT Add-on settings saved.");
    }

    private async Task SaveSettingsAsync(ChatAddonSettings settings)
    {
        if (string.IsNullOrWhiteSpace(_settingsPath))
            throw new InvalidOperationException("ChatGPT Add-on settings path is not available.");

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json, Encoding.UTF8);

        _settings = settings;
        _settingsLastWriteUtc = File.GetLastWriteTimeUtc(_settingsPath);
        UpdateViews();
    }

    private static string ReadValue(IReadOnlyDictionary<string, string?> values, string key, string fallback)
    {
        return values.TryGetValue(key, out var value) && value != null ? value : fallback;
    }

    private static List<string> ReadMultilineValues(IReadOnlyDictionary<string, string?> values, string key, IReadOnlyCollection<string> fallback)
    {
        if (!values.TryGetValue(key, out var rawValue) || rawValue == null)
            return fallback.ToList();

        return rawValue
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(prompt => prompt.Trim())
            .Where(prompt => !string.IsNullOrWhiteSpace(prompt))
            .ToList();
    }

    private void OnThemeChanged()
    {
        _isLight = _themeService?.IsLight ?? false;
        UpdateViews();
    }

    private Brush GetAccentBrush()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(56, 118, 221)
            : Color.FromRgb(92, 159, 255));
    }

    private Brush GetPrimaryForeground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(28, 34, 42)
            : Color.FromRgb(241, 244, 248));
    }

    private Brush GetSecondaryForeground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(98, 106, 118)
            : Color.FromRgb(180, 186, 194));
    }

    private Brush GetMutedForeground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(124, 130, 140)
            : Color.FromRgb(146, 154, 164));
    }

    private Brush GetInputBrush()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(238, 241, 246)
            : Color.FromRgb(36, 40, 48));
    }

    private Brush GetChipBrush()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(229, 234, 240)
            : Color.FromRgb(52, 58, 68));
    }

    public override Task ShutdownAsync()
    {
        if (_themeService != null)
            _themeService.ThemeChanged -= OnThemeChanged;

        return base.ShutdownAsync();
    }

    private sealed record ChatView(
        Border Root,
        TextBlock TitleText,
        TextBlock SummaryText,
        TextBox PromptBox,
        WrapPanel PresetPanel,
        TextBlock StatusText,
        Button ConfigButton,
        Button CopyButton,
        Button OpenButton,
        Button ClearButton,
        bool Compact);

    private sealed class ChatAddonSettings
    {
        [JsonPropertyName("chatUrl")]
        public string ChatUrl { get; set; } = "https://chatgpt.com/";

        [JsonPropertyName("quickPrompts")]
        public List<string> QuickPrompts { get; set; } = new();

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(ChatUrl))
            {
                ChatUrl = "https://chatgpt.com/";
            }

            QuickPrompts = QuickPrompts
                .Where(prompt => !string.IsNullOrWhiteSpace(prompt))
                .Select(prompt => prompt.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();

            if (QuickPrompts.Count == 0)
            {
                QuickPrompts = CreateDefault().QuickPrompts;
            }
        }

        public static ChatAddonSettings CreateDefault()
        {
            var settings = new ChatAddonSettings
            {
                QuickPrompts =
                [
                    "Summarize this clearly.",
                    "Rewrite this to sound more polished.",
                    "Give me three creative directions for this.",
                    "Turn this into an actionable checklist."
                ]
            };
            settings.Normalize();
            return settings;
        }
    }
}
