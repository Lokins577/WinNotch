using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch.Models;
using WinNotch.Plugins;

namespace ClipboardStackPlugin;

[WinNotchPlugin("com.winnotch.clipboardstack", "Clipboard Stack", "1.0.2", "WinNotch Team")]
[PluginPermission("clipboard", "Required to read recent clipboard text and file copies.")]
public sealed class ClipboardStackPlugin : PluginBase, IUIPlugin
{
    private const int MaxEntries = 5;
    private const double OpenViewportHeight = 84;
    private const double VerticalViewportHeight = 72;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1100);
    private static readonly Uri RoundedScrollBarStyleUri =
        new("pack://application:,,,/WinNotch;component/Views/Components/RoundedScrollBarStyle.xaml", UriKind.Absolute);

    public override string Id => "com.winnotch.clipboardstack";
    public override string Name => "Clipboard Stack";
    public override string Version => "1.0.2";
    public override string Author => "WinNotch Team";
    public override string Description => "Keeps the last five copied text snippets and file lists ready to copy back into the clipboard.";
    public override string MinimumWinNotchVersion => "0.3.8";

    private IPluginThemeService? _themeService;
    private DispatcherTimer? _pollTimer;
    private ClipboardStackView? _openView;
    private ClipboardStackView? _verticalView;
    private readonly List<ClipboardEntry> _entries = new();
    private string? _lastSignature;
    private string _statusText = "Watching the clipboard.";
    private string _detailText = "Copy text or files to start building your stack.";
    private bool _isLight;

    public override async Task InitializeAsync(IPluginContext context)
    {
        await base.InitializeAsync(context);

        _themeService = context.Theme;
        _isLight = _themeService.IsLight;
        _themeService.ThemeChanged += OnThemeChanged;

        _pollTimer = new DispatcherTimer { Interval = PollInterval };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();

        CaptureClipboardSnapshot(forceStatusUpdate: true);
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
            CaptureClipboardSnapshot(forceStatusUpdate: true);
        }
    }

    private UIElement BuildOpenView()
    {
        if (_openView != null)
            return _openView.Root;

        _openView = CreateView(width: 336, compact: false);
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

    private ClipboardStackView CreateView(double width, bool compact)
    {
        var root = new Border
        {
            Width = width,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 4, 0, 0),
            BorderThickness = new Thickness(0)
        };
        EnsureRoundedScrollResources(root);

        var outer = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel();

        var titleText = new TextBlock
        {
            Text = "Clipboard Stack",
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

        var clearButton = new Button
        {
            Content = "Clear",
            Padding = new Thickness(10, 3, 10, 3),
            FontSize = 10,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top
        };
        clearButton.Click += (_, _) => ClearStack();
        Grid.SetColumn(clearButton, 1);
        header.Children.Add(clearButton);

        outer.Children.Add(header);

        var statusText = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        outer.Children.Add(statusText);

        var detailText = new TextBlock
        {
            FontSize = 10,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        outer.Children.Add(detailText);

        var entryHost = new StackPanel();
        var entryScroll = new ScrollViewer
        {
            Content = entryHost,
            Height = compact ? VerticalViewportHeight : OpenViewportHeight,
            Margin = new Thickness(0, 10, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = true,
            Padding = new Thickness(0, 0, 2, 0)
        };

        if (root.TryFindResource("RoundedScrollViewer") is Style roundedStyle)
        {
            entryScroll.Style = roundedStyle;
        }

        outer.Children.Add(entryScroll);

        root.Child = outer;

        return new ClipboardStackView(root, titleText, summaryText, statusText, detailText, entryHost, entryScroll, clearButton, compact);
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        CaptureClipboardSnapshot(forceStatusUpdate: false);
    }

    private void CaptureClipboardSnapshot(bool forceStatusUpdate)
    {
        try
        {
            var snapshot = TryReadClipboardEntry();
            if (snapshot == null)
            {
                if (forceStatusUpdate && _entries.Count == 0)
                {
                    _statusText = "Watching the clipboard.";
                    _detailText = "Copy text or files to start building your stack.";
                    UpdateViews();
                }

                return;
            }

            if (snapshot.Signature == _lastSignature && !forceStatusUpdate)
                return;

            _lastSignature = snapshot.Signature;
            InsertOrPromote(snapshot);

            _statusText = $"{_entries.Count} item{(_entries.Count == 1 ? string.Empty : "s")} ready";
            _detailText = snapshot.Kind switch
            {
                ClipboardEntryKind.Files => snapshot.SecondaryText,
                _ => "Latest copy added to the top of the stack."
            };

            UpdateViews();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            if (forceStatusUpdate)
            {
                _statusText = "Clipboard is busy right now.";
                _detailText = "Another app is holding the clipboard. Try again in a moment.";
                UpdateViews();
            }
        }
    }

    private ClipboardEntry? TryReadClipboardEntry()
    {
        if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
        {
            var text = Clipboard.GetText(TextDataFormat.UnicodeText);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
            if (normalized.Length == 0)
                return null;

            var preview = CreateTextPreview(normalized);
            var secondary = $"{normalized.Length} characters  {DateTime.Now:HH:mm}";
            return new ClipboardEntry(
                Signature: $"text:{normalized}",
                Kind: ClipboardEntryKind.Text,
                Title: preview,
                SecondaryText: secondary,
                TextValue: normalized,
                FilePaths: Array.Empty<string>(),
                CapturedAt: DateTime.Now);
        }

        if (Clipboard.ContainsFileDropList())
        {
            var list = Clipboard.GetFileDropList();
            var filePaths = list.Cast<string>()
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();

            if (filePaths.Length == 0)
                return null;

            var label = filePaths.Length == 1
                ? Path.GetFileName(filePaths[0])
                : $"{filePaths.Length} files";

            if (string.IsNullOrWhiteSpace(label))
                label = filePaths[0];

            var detail = filePaths.Length == 1
                ? filePaths[0]
                : string.Join("  ", filePaths.Take(2).Select(Path.GetFileName));

            return new ClipboardEntry(
                Signature: $"files:{string.Join("|", filePaths)}",
                Kind: ClipboardEntryKind.Files,
                Title: label,
                SecondaryText: detail,
                TextValue: null,
                FilePaths: filePaths,
                CapturedAt: DateTime.Now);
        }

        return null;
    }

    private void InsertOrPromote(ClipboardEntry entry)
    {
        var existingIndex = _entries.FindIndex(item => string.Equals(item.Signature, entry.Signature, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            var existing = _entries[existingIndex] with { CapturedAt = entry.CapturedAt };
            _entries.RemoveAt(existingIndex);
            _entries.Insert(0, existing);
        }
        else
        {
            _entries.Insert(0, entry);
        }

        while (_entries.Count > MaxEntries)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }
    }

    private void ClearStack()
    {
        _entries.Clear();
        _statusText = "Clipboard stack cleared.";
        _detailText = "Copy something new to start filling it again.";
        UpdateViews();
    }

    private async Task RestoreEntryAsync(ClipboardEntry entry)
    {
        try
        {
            switch (entry.Kind)
            {
                case ClipboardEntryKind.Files:
                    var collection = new StringCollection();
                    collection.AddRange(entry.FilePaths);
                    Clipboard.SetFileDropList(collection);
                    break;

                default:
                    Clipboard.SetText(entry.TextValue ?? string.Empty, TextDataFormat.UnicodeText);
                    break;
            }

            InsertOrPromote(entry with { CapturedAt = DateTime.Now });
            _lastSignature = entry.Signature;
            _statusText = "Copied back to the clipboard.";
            _detailText = entry.Kind == ClipboardEntryKind.Files
                ? $"{entry.FilePaths.Length} file item{(entry.FilePaths.Length == 1 ? string.Empty : "s")} restored."
                : "That snippet is ready to paste again.";
            UpdateViews();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _statusText = "Clipboard is busy right now.";
            _detailText = "Another app is holding the clipboard. Try restoring again in a moment.";
            UpdateViews();
        }

        await Task.CompletedTask;
    }

    private void UpdateViews()
    {
        UpdateView(_openView);
        UpdateView(_verticalView);
    }

    private void UpdateView(ClipboardStackView? view)
    {
        if (view == null)
            return;

        view.SummaryText.Text = _entries.Count == 0
            ? "Last 5 copies"
            : $"{_entries.Count}/{MaxEntries} saved";
        view.StatusText.Text = _statusText;
        view.DetailText.Text = _detailText;
        view.EntryHost.Children.Clear();

        if (_entries.Count == 0)
        {
            view.EntryHost.Children.Add(CreateEmptyState(view.Compact));
        }
        else
        {
            foreach (var entry in _entries)
            {
                view.EntryHost.Children.Add(CreateEntryCard(entry, view.Compact));
            }
        }

        ApplyTheme(view);
    }

    private UIElement CreateEmptyState(bool compact)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 2, 0, 0)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        row.Children.Add(new Border
        {
            Width = 2,
            Margin = new Thickness(0, 1, 10, 1),
            Background = GetSeparatorBrush()
        });

        var stack = new StackPanel();

        var title = new TextBlock
        {
            Text = "Your stack is empty.",
            FontSize = compact ? 11 : 12,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetPrimaryForeground()
        };
        stack.Children.Add(title);

        var body = new TextBlock
        {
            Text = "Copy text or file selections anywhere in Windows and they will appear here.",
            FontSize = 10,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetSecondaryForeground()
        };
        stack.Children.Add(body);

        Grid.SetColumn(stack, 1);
        row.Children.Add(stack);
        return row;
    }

    private UIElement CreateEntryCard(ClipboardEntry entry, bool compact)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 0, 0, 10)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new Border
        {
            Width = 2,
            Margin = new Thickness(0, 2, 10, 2),
            Background = ReferenceEquals(entry, _entries.FirstOrDefault())
                ? GetAccentBrush()
                : GetSeparatorBrush()
        });

        var textStack = new StackPanel();

        var title = new TextBlock
        {
            Text = entry.Title,
            FontSize = compact ? 11 : 12,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetPrimaryForeground()
        };
        textStack.Children.Add(title);

        var meta = new TextBlock
        {
            Text = $"{GetKindLabel(entry.Kind)}  {entry.SecondaryText}",
            FontSize = 10,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetSecondaryForeground()
        };
        textStack.Children.Add(meta);

        if (!compact && entry.Kind == ClipboardEntryKind.Text && !string.IsNullOrWhiteSpace(entry.TextValue))
        {
            var preview = new TextBlock
            {
                Text = entry.TextValue,
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 34,
                Foreground = GetMutedForeground()
            };
            textStack.Children.Add(preview);
        }

        Grid.SetColumn(textStack, 1);
        row.Children.Add(textStack);

        var copyButton = new Button
        {
            Content = "Copy",
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 10,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(10, 0, 0, 0),
            Background = Brushes.Transparent,
            Foreground = GetAccentBrush(),
            FontWeight = FontWeights.SemiBold
        };
        copyButton.Click += async (_, _) =>
        {
            copyButton.IsEnabled = false;
            try
            {
                await RestoreEntryAsync(entry);
            }
            finally
            {
                copyButton.IsEnabled = true;
            }
        };

        Grid.SetColumn(copyButton, 2);
        row.Children.Add(copyButton);

        row.ToolTip = entry.Kind == ClipboardEntryKind.Text
            ? entry.TextValue
            : string.Join(Environment.NewLine, entry.FilePaths);

        return row;
    }

    private void ApplyTheme(ClipboardStackView view)
    {
        view.Root.Background = Brushes.Transparent;
        view.Root.BorderBrush = Brushes.Transparent;
        view.Root.BorderThickness = new Thickness(0);
        view.TitleText.Foreground = GetPrimaryForeground();
        view.SummaryText.Foreground = GetSecondaryForeground();
        view.StatusText.Foreground = GetPrimaryForeground();
        view.DetailText.Foreground = GetSecondaryForeground();
        view.ClearButton.Background = Brushes.Transparent;
        view.ClearButton.Foreground = GetSecondaryForeground();
    }

    private static void EnsureRoundedScrollResources(Border root)
    {
        root.Resources ??= new ResourceDictionary();

        if (root.Resources.MergedDictionaries.Any(dictionary =>
                dictionary.Source?.OriginalString?.Contains("RoundedScrollBarStyle.xaml", StringComparison.OrdinalIgnoreCase) == true))
        {
            return;
        }

        try
        {
            root.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = RoundedScrollBarStyleUri
            });
        }
        catch
        {
            // Best effort only. The ScrollViewer still works with the default style.
        }
    }

    private void OnThemeChanged()
    {
        _isLight = _themeService?.IsLight ?? false;
        UpdateViews();
    }

    private static string CreateTextPreview(string text)
    {
        var singleLine = text.Replace('\n', ' ').Trim();
        if (singleLine.Length <= 72)
            return singleLine;

        return singleLine[..69] + "...";
    }

    private static string GetKindLabel(ClipboardEntryKind kind)
    {
        return kind switch
        {
            ClipboardEntryKind.Files => "Files",
            _ => "Text"
        };
    }

    private Brush GetSeparatorBrush()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(210, 216, 226)
            : Color.FromRgb(82, 88, 98));
    }

    private Brush GetAccentBrush()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(53, 116, 219)
            : Color.FromRgb(78, 148, 245));
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

    private Brush GetMutedForeground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(122, 129, 139)
            : Color.FromRgb(148, 156, 166));
    }

    public override Task ShutdownAsync()
    {
        if (_themeService != null)
            _themeService.ThemeChanged -= OnThemeChanged;

        if (_pollTimer != null)
            _pollTimer.Tick -= OnPollTick;

        _pollTimer?.Stop();
        _pollTimer = null;

        return base.ShutdownAsync();
    }

    public override void Dispose()
    {
        _pollTimer?.Stop();
        _pollTimer = null;
        base.Dispose();
    }

    private sealed record ClipboardStackView(
        Border Root,
        TextBlock TitleText,
        TextBlock SummaryText,
        TextBlock StatusText,
        TextBlock DetailText,
        StackPanel EntryHost,
        ScrollViewer EntryScroll,
        Button ClearButton,
        bool Compact);

    private sealed record ClipboardEntry(
        string Signature,
        ClipboardEntryKind Kind,
        string Title,
        string SecondaryText,
        string? TextValue,
        string[] FilePaths,
        DateTime CapturedAt);

    private enum ClipboardEntryKind
    {
        Text,
        Files
    }
}
