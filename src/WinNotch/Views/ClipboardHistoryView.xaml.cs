using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using WinNotch.Clipboard;
using WinNotch.Services;
using Brush = System.Windows.Media.Brush;

namespace WinNotch.Views;

public partial class ClipboardHistoryView : UserControl
{
    private ClipboardHistoryService? _service;
    private bool _compact;

    public ClipboardHistoryView()
    {
        InitializeComponent();
    }

    public void Bind(ClipboardHistoryService service, bool compact = false)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_service != null)
            _service.Updated -= OnUpdated;
        _service = service;
        _compact = compact;
        _service.Updated += OnUpdated;
        SearchBox.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Render();
    }

    private void OnUpdated(ClipboardHistorySnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Render);
            return;
        }
        Render();
    }

    private void Render()
    {
        if (_service == null) return;
        var items = _service.Search(SearchBox.Text, _compact ? 3 : 8);
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ItemHost.Children.Clear();
        foreach (var item in items)
            ItemHost.Children.Add(CreateRow(item));
    }

    private FrameworkElement CreateRow(ClipboardHistoryItem item)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = CreatePreview(item);
        grid.Children.Add(icon);
        var text = new TextBlock
        {
            Text = PreviewText(item),
            FontSize = 10,
            MaxHeight = _compact ? 30 : 42,
            Margin = new Thickness(8, 0, 8, 0),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = GetBrush("TextSecondaryBrush", System.Windows.Media.Brushes.LightGray)
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        actions.Children.Add(ActionButton("Copy", (_, _) => _service?.CopyToClipboard(item)));
        actions.Children.Add(ActionButton(item.IsPinned ? "Unpin" : "Pin",
            (_, _) => _service?.SetPinned(item.Id, !item.IsPinned)));
        actions.Children.Add(ActionButton("×", (_, _) => _service?.Remove(item.Id)));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        return grid;
    }

    private static FrameworkElement CreatePreview(ClipboardHistoryItem item)
    {
        if (item.Kind == ClipboardItemKind.Image && item.ImagePng is { Length: > 0 } png)
        {
            using var stream = new MemoryStream(png, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 48;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return new System.Windows.Controls.Image
            {
                Source = image,
                Width = 34,
                Height = 34,
                Stretch = System.Windows.Media.Stretch.UniformToFill
            };
        }

        return new TextBlock
        {
            Text = item.Kind == ClipboardItemKind.Files ? "▤" : "T",
            Width = 28,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            Foreground = GetBrush("TextSubtleBrush", System.Windows.Media.Brushes.Gray)
        };
    }

    private static string PreviewText(ClipboardHistoryItem item)
    {
        return item.Kind switch
        {
            ClipboardItemKind.Text => item.Text ?? string.Empty,
            ClipboardItemKind.Files => string.Join(", ", item.Files.Select(Path.GetFileName)),
            ClipboardItemKind.Image => "Image",
            _ => string.Empty
        };
    }

    private static Button ActionButton(string text, RoutedEventHandler action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(2, 0, 0, 0),
            FontSize = 9
        };
        button.Click += action;
        return button;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Render();
    private void OnClear(object sender, RoutedEventArgs e) => _service?.ClearUnpinned();

    private static Brush GetBrush(string key, Brush fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
