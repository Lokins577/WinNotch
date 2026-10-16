using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch.Models;
using WinNotch.Plugins;

namespace DownloadsWatcherPlugin;

[WinNotchPlugin("com.winnotch.downloadswatcher", "Downloads Watcher", "1.0.0", "WinNotch Team")]
public sealed class DownloadsWatcherPlugin : PluginBase, IUIPlugin
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(4);
    private static readonly HashSet<string> ActiveDownloadExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload",
        ".part",
        ".partial",
        ".download",
        ".opdownload"
    };

    public override string Id => "com.winnotch.downloadswatcher";
    public override string Name => "Downloads Watcher";
    public override string Version => "1.0.0";
    public override string Author => "WinNotch Team";
    public override string Description => "Tracks active and recent downloads with quick open actions.";
    public override string MinimumWinNotchVersion => "0.3.8";

    private IPluginThemeService? _themeService;
    private DispatcherTimer? _refreshTimer;
    private DownloadsView? _openView;
    private DownloadsView? _verticalView;
    private DownloadsSnapshot _snapshot = DownloadsSnapshot.Empty("Loading downloads...");
    private string? _downloadsPath;
    private bool _isLight;

    public override async Task InitializeAsync(IPluginContext context)
    {
        await base.InitializeAsync(context);

        _themeService = context.Theme;
        _isLight = _themeService.IsLight;
        _themeService.ThemeChanged += OnThemeChanged;

        _downloadsPath = ResolveDownloadsPath();

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshTimer.Start();

        RefreshSnapshot();
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
            RefreshSnapshot();
        }
    }

    private UIElement BuildOpenView()
    {
        if (_openView != null)
            return _openView.Root;

        _openView = CreateView(width: 344, compact: false);
        UpdateView(_openView);
        return _openView.Root;
    }

    private UIElement BuildVerticalView()
    {
        if (_verticalView != null)
            return _verticalView.Root;

        _verticalView = CreateView(width: 176, compact: true);
        _verticalView.Root.Margin = new Thickness(0, 0, 0, 2);
        UpdateView(_verticalView);
        return _verticalView.Root;
    }

    private DownloadsView CreateView(double width, bool compact)
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
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel();

        var titleText = new TextBlock
        {
            Text = "Downloads",
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

        var refreshButton = CreateActionButton("Refresh");
        refreshButton.Click += (_, _) => RefreshSnapshot();
        Grid.SetColumn(refreshButton, 1);
        header.Children.Add(refreshButton);

        var folderButton = CreateActionButton("Folder");
        folderButton.Margin = new Thickness(8, 0, 0, 0);
        folderButton.Click += (_, _) => OpenDownloadsFolder();
        Grid.SetColumn(folderButton, 2);
        header.Children.Add(folderButton);

        outer.Children.Add(header);

        var activeHeaderText = new TextBlock
        {
            Text = "In progress",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 0)
        };
        outer.Children.Add(activeHeaderText);

        var activeItemsPanel = new StackPanel
        {
            Margin = new Thickness(0, 6, 0, 0)
        };
        outer.Children.Add(activeItemsPanel);

        var separator = CreateSeparator();
        outer.Children.Add(separator);

        var recentHeaderText = new TextBlock
        {
            Text = "Recent",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold
        };
        outer.Children.Add(recentHeaderText);

        var recentItemsPanel = new StackPanel
        {
            Margin = new Thickness(0, 6, 0, 0)
        };
        outer.Children.Add(recentItemsPanel);

        var footerText = new TextBlock
        {
            FontSize = 9,
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        outer.Children.Add(footerText);

        root.Child = outer;

        return new DownloadsView(
            root,
            titleText,
            summaryText,
            activeHeaderText,
            activeItemsPanel,
            separator,
            recentHeaderText,
            recentItemsPanel,
            footerText,
            refreshButton,
            folderButton,
            compact);
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        RefreshSnapshot();
    }

    private void RefreshSnapshot()
    {
        try
        {
            _snapshot = BuildSnapshot();
        }
        catch (Exception ex)
        {
            _snapshot = DownloadsSnapshot.Empty("Downloads unavailable.");
            Context?.Log($"Downloads Watcher refresh failed: {ex.Message}", PluginLogLevel.Warning);
        }

        UpdateViews();
    }

    private DownloadsSnapshot BuildSnapshot()
    {
        if (string.IsNullOrWhiteSpace(_downloadsPath))
        {
            return DownloadsSnapshot.Empty("Downloads folder unavailable.");
        }

        var directory = new DirectoryInfo(_downloadsPath);
        if (!directory.Exists)
        {
            return DownloadsSnapshot.Empty("Downloads folder unavailable.");
        }

        var files = directory.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(file => !file.Attributes.HasFlag(FileAttributes.Hidden) && !file.Attributes.HasFlag(FileAttributes.System))
            .ToList();

        var activeItems = files
            .Where(IsActiveDownloadFile)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(3)
            .Select(file => CreateItem(file, isActive: true))
            .ToList();

        var recentItems = files
            .Where(file => !IsActiveDownloadFile(file))
            .OrderByDescending(GetRecencyUtc)
            .Take(4)
            .Select(file => CreateItem(file, isActive: false))
            .ToList();

        var summary = activeItems.Count > 0
            ? $"{activeItems.Count} active  {recentItems.Count} recent"
            : recentItems.Count > 0
                ? $"No active transfers  {recentItems.Count} recent"
                : "Watching your Downloads folder";

        return new DownloadsSnapshot(
            activeItems,
            recentItems,
            summary,
            _downloadsPath,
            DateTime.Now);
    }

    private DownloadItemInfo CreateItem(FileInfo file, bool isActive)
    {
        var modifiedAt = file.LastWriteTime;
        var displayName = isActive ? NormalizeActiveName(file.Name) : file.Name;
        var detailText = isActive
            ? $"{FormatSize(file.Length)} written  Updated {modifiedAt:HH:mm:ss}"
            : $"{FormatSize(file.Length)}  Finished {modifiedAt:HH:mm}";

        return new DownloadItemInfo(
            displayName,
            file.FullName,
            isActive,
            detailText,
            isActive ? "Folder" : "Open");
    }

    private static bool IsActiveDownloadFile(FileInfo file)
    {
        return ActiveDownloadExtensions.Contains(file.Extension);
    }

    private static DateTime GetRecencyUtc(FileInfo file)
    {
        return file.LastWriteTimeUtc > file.CreationTimeUtc
            ? file.LastWriteTimeUtc
            : file.CreationTimeUtc;
    }

    private static string NormalizeActiveName(string fileName)
    {
        foreach (var extension in ActiveDownloadExtensions)
        {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return fileName[..^extension.Length];
            }
        }

        return fileName;
    }

    private void UpdateViews()
    {
        UpdateView(_openView);
        UpdateView(_verticalView);
    }

    private void UpdateView(DownloadsView? view)
    {
        if (view == null)
            return;

        view.SummaryText.Text = _snapshot.Summary;
        view.FooterText.Text = _snapshot.FolderPath == null
            ? "Downloads folder could not be found on this system."
            : $"Watching {_snapshot.FolderPath}  Updated {_snapshot.UpdatedAt:HH:mm:ss}";

        PopulateItems(view.ActiveItemsPanel, _snapshot.ActiveItems, view.Compact, emptyText: "No active downloads.");
        PopulateItems(view.RecentItemsPanel, _snapshot.RecentItems, view.Compact, emptyText: "No recent files yet.");

        ApplyTheme(view);
    }

    private void PopulateItems(Panel panel, IReadOnlyList<DownloadItemInfo> items, bool compact, string emptyText)
    {
        panel.Children.Clear();

        if (items.Count == 0)
        {
            panel.Children.Add(CreateEmptyText(emptyText));
            return;
        }

        var maxItems = compact ? Math.Min(items.Count, 2) : items.Count;
        for (var index = 0; index < maxItems; index++)
        {
            panel.Children.Add(CreateDownloadRow(items[index], compact));
        }
    }

    private UIElement CreateDownloadRow(DownloadItemInfo item, bool compact)
    {
        var row = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, compact ? 8 : 10)
        };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel();

        var nameText = new TextBlock
        {
            Text = item.DisplayName,
            FontSize = compact ? 11 : 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = GetPrimaryForeground()
        };
        textStack.Children.Add(nameText);

        var detailText = new TextBlock
        {
            Text = item.DetailText,
            FontSize = compact ? 9 : 10,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetSecondaryForeground()
        };
        textStack.Children.Add(detailText);

        Grid.SetColumn(textStack, 0);
        header.Children.Add(textStack);

        var actionButton = CreateActionButton(item.ActionLabel);
        actionButton.Margin = new Thickness(8, 0, 0, 0);
        actionButton.Click += (_, _) => OpenItem(item);
        Grid.SetColumn(actionButton, 1);
        header.Children.Add(actionButton);

        row.Children.Add(header);

        if (item.IsActive)
        {
            var progress = new ProgressBar
            {
                Height = 4,
                Margin = new Thickness(0, 6, 0, 0),
                Minimum = 0,
                Maximum = 100,
                IsIndeterminate = true,
                Foreground = GetAccentBrush(),
                Background = GetTrackBrush(),
                BorderThickness = new Thickness(0)
            };
            row.Children.Add(progress);
        }
        else
        {
            row.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 6, 0, 0),
                Background = GetTrackBrush()
            });
        }

        return row;
    }

    private TextBlock CreateEmptyText(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetMutedForeground()
        };
    }

    private static Border CreateSeparator()
    {
        return new Border
        {
            Height = 1,
            Margin = new Thickness(0, 10, 0, 10)
        };
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

    private void ApplyTheme(DownloadsView view)
    {
        view.Root.Background = Brushes.Transparent;
        view.Root.BorderBrush = Brushes.Transparent;
        view.Root.BorderThickness = new Thickness(0);

        view.TitleText.Foreground = GetPrimaryForeground();
        view.SummaryText.Foreground = GetSecondaryForeground();
        view.ActiveHeaderText.Foreground = GetSecondaryForeground();
        view.RecentHeaderText.Foreground = GetSecondaryForeground();
        view.Separator.Background = GetTrackBrush();
        view.FooterText.Foreground = GetMutedForeground();

        ApplyButtonTheme(view.RefreshButton);
        ApplyButtonTheme(view.FolderButton);
    }

    private void ApplyButtonTheme(Button button)
    {
        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.Foreground = GetAccentBrush();
        button.FontWeight = FontWeights.SemiBold;
    }

    private void OpenItem(DownloadItemInfo item)
    {
        try
        {
            var target = item.IsActive || !File.Exists(item.FilePath)
                ? _downloadsPath
                : item.FilePath;

            if (string.IsNullOrWhiteSpace(target))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch
        {
            // Best effort only.
        }
    }

    private void OpenDownloadsFolder()
    {
        if (string.IsNullOrWhiteSpace(_downloadsPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _downloadsPath,
                UseShellExecute = true
            });
        }
        catch
        {
            // Best effort only.
        }
    }

    private void OnThemeChanged()
    {
        _isLight = _themeService?.IsLight ?? false;
        UpdateViews();
    }

    private Brush GetAccentBrush()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(52, 120, 222)
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
            ? Color.FromRgb(97, 105, 117)
            : Color.FromRgb(180, 186, 194));
    }

    private Brush GetMutedForeground()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(124, 130, 140)
            : Color.FromRgb(146, 154, 164));
    }

    private Brush GetTrackBrush()
    {
        return new SolidColorBrush(_isLight
            ? Color.FromRgb(214, 220, 228)
            : Color.FromRgb(80, 86, 96));
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var index = 0;

        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        var format = index == 0 ? "0" : value >= 100 ? "0" : "0.0";
        return $"{value.ToString(format)} {units[index]}";
    }

    private static string? ResolveDownloadsPath()
    {
        try
        {
            var result = SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out var pathPointer);
            if (result == 0 && pathPointer != IntPtr.Zero)
            {
                try
                {
                    return Marshal.PtrToStringUni(pathPointer);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pathPointer);
                }
            }
        }
        catch
        {
            // Fall back below.
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        return Directory.Exists(fallback) ? fallback : null;
    }

    public override Task ShutdownAsync()
    {
        if (_themeService != null)
            _themeService.ThemeChanged -= OnThemeChanged;

        if (_refreshTimer != null)
            _refreshTimer.Tick -= OnRefreshTimerTick;

        _refreshTimer?.Stop();
        _refreshTimer = null;

        return base.ShutdownAsync();
    }

    public override void Dispose()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
        base.Dispose();
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);

    private sealed record DownloadsView(
        Border Root,
        TextBlock TitleText,
        TextBlock SummaryText,
        TextBlock ActiveHeaderText,
        StackPanel ActiveItemsPanel,
        Border Separator,
        TextBlock RecentHeaderText,
        StackPanel RecentItemsPanel,
        TextBlock FooterText,
        Button RefreshButton,
        Button FolderButton,
        bool Compact);

    private sealed record DownloadsSnapshot(
        IReadOnlyList<DownloadItemInfo> ActiveItems,
        IReadOnlyList<DownloadItemInfo> RecentItems,
        string Summary,
        string? FolderPath,
        DateTime UpdatedAt)
    {
        public static DownloadsSnapshot Empty(string summary)
        {
            return new DownloadsSnapshot(
                Array.Empty<DownloadItemInfo>(),
                Array.Empty<DownloadItemInfo>(),
                summary,
                null,
                DateTime.Now);
        }
    }

    private sealed record DownloadItemInfo(
        string DisplayName,
        string FilePath,
        bool IsActive,
        string DetailText,
        string ActionLabel);
}
