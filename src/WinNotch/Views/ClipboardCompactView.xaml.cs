using System.Windows;
using System.Windows.Controls;
using WinNotch.Clipboard;
using WinNotch.Services;

namespace WinNotch.Views;

public partial class ClipboardCompactView : UserControl
{
    private ClipboardHistoryService? _service;

    public ClipboardCompactView()
    {
        InitializeComponent();
    }

    public void Bind(ClipboardHistoryService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (_service != null) _service.Updated -= OnUpdated;
        _service = service;
        _service.Updated += OnUpdated;
        Render(service.Snapshot);
    }

    private void OnUpdated(ClipboardHistorySnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Render(snapshot));
            return;
        }
        Render(snapshot);
    }

    private void Render(ClipboardHistorySnapshot snapshot)
    {
        var item = snapshot.Items.FirstOrDefault();
        Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
        PreviewText.Text = item?.Kind switch
        {
            ClipboardItemKind.Text => item.Text,
            ClipboardItemKind.Files => $"{item.Files.Length} files",
            ClipboardItemKind.Image => "Image",
            _ => string.Empty
        };
    }
}
