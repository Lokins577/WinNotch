using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WinNotch.Clipboard;
using WinNotch.Features;

namespace WinNotch.Services;

public sealed class ClipboardHistoryService : IFeatureModule, IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private readonly ClipboardHistory _history;
    private readonly IClipboardHistoryStore _store;
    private readonly ClipboardPreferences _preferences;
    private HwndSource? _source;
    private uint _ignoredSequence;

    public ClipboardHistoryService(
        ClipboardPreferences preferences,
        IClipboardHistoryStore store)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _history = new ClipboardHistory(preferences, store.Load());
    }

    public FeatureId Id => BuiltInFeatures.Clipboard.Id;
    public ClipboardHistorySnapshot Snapshot => new(_history.Items.ToArray());
    public event Action<ClipboardHistorySnapshot>? Updated;

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_source != null) return ValueTask.CompletedTask;

        _source = new HwndSource(new HwndSourceParameters("WinNotch.ClipboardListener")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        });
        _source.AddHook(WindowProcedure);
        if (!AddClipboardFormatListener(_source.Handle))
            throw new InvalidOperationException("Windows rejected clipboard listener registration.");
        Publish();
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (_source == null) return ValueTask.CompletedTask;
        RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
        _source = null;
        Persist();
        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<ClipboardHistoryItem> Search(string? query, int limit = 50)
        => _history.Search(query, limit);

    public void SetPinned(Guid id, bool pinned)
    {
        if (!_history.SetPinned(id, pinned)) return;
        PersistAndPublish();
    }

    public void Remove(Guid id)
    {
        if (!_history.Remove(id)) return;
        PersistAndPublish();
    }

    public void ClearUnpinned()
    {
        _history.ClearUnpinned();
        PersistAndPublish();
    }

    public void CopyToClipboard(ClipboardHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        switch (item.Kind)
        {
            case ClipboardItemKind.Text when item.Text != null:
                System.Windows.Clipboard.SetText(item.Text);
                break;
            case ClipboardItemKind.Files when item.Files.Length > 0:
                var files = new StringCollection();
                files.AddRange(item.Files);
                System.Windows.Clipboard.SetFileDropList(files);
                break;
            case ClipboardItemKind.Image when item.ImagePng is { Length: > 0 } png:
                using (var stream = new MemoryStream(png, writable: false))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    System.Windows.Clipboard.SetImage(image);
                }
                break;
        }
        _ignoredSequence = GetClipboardSequenceNumber();
    }

    private IntPtr WindowProcedure(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmClipboardUpdate)
        {
            handled = true;
            _ = CaptureWithRetryAsync();
        }
        return IntPtr.Zero;
    }

    private async Task CaptureWithRetryAsync()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                uint sequence = GetClipboardSequenceNumber();
                if (sequence != 0 && sequence == _ignoredSequence)
                {
                    _ignoredSequence = 0;
                    return;
                }

                var item = CaptureCurrentClipboard();
                if (item != null && _history.Add(item))
                    PersistAndPublish();
                return;
            }
            catch (COMException) when (attempt < 2)
            {
                await Task.Delay(60);
            }
            catch (ExternalException) when (attempt < 2)
            {
                await Task.Delay(60);
            }
            catch (Exception exception)
            {
                Updated?.Invoke(new ClipboardHistorySnapshot(_history.Items.ToArray(), exception.Message));
                return;
            }
        }
    }

    private ClipboardHistoryItem? CaptureCurrentClipboard()
    {
        string? sourceApplication = GetForegroundProcessName();
        if (sourceApplication != null && _preferences.IgnoredApplications.Contains(
                sourceApplication, StringComparer.OrdinalIgnoreCase))
            return null;

        if (_preferences.CaptureFiles && System.Windows.Clipboard.ContainsFileDropList())
        {
            var files = System.Windows.Clipboard.GetFileDropList().Cast<string>().ToArray();
            return new ClipboardHistoryItem
            {
                Kind = ClipboardItemKind.Files,
                Files = files,
                SourceApplication = sourceApplication
            };
        }

        if (_preferences.CaptureImages && System.Windows.Clipboard.ContainsImage())
        {
            var image = System.Windows.Clipboard.GetImage();
            if (image != null)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                return new ClipboardHistoryItem
                {
                    Kind = ClipboardItemKind.Image,
                    ImagePng = stream.ToArray(),
                    SourceApplication = sourceApplication
                };
            }
        }

        if (System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.UnicodeText))
        {
            return new ClipboardHistoryItem
            {
                Kind = ClipboardItemKind.Text,
                Text = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText),
                SourceApplication = sourceApplication
            };
        }

        return null;
    }

    private static string? GetForegroundProcessName()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0) return null;
        try { return Process.GetProcessById((int)processId).ProcessName; }
        catch { return null; }
    }

    private void PersistAndPublish()
    {
        Persist();
        Publish();
    }

    private void Persist()
    {
        _store.Save(_history.Items);
    }

    private void Publish()
    {
        Updated?.Invoke(new ClipboardHistorySnapshot(_history.Items.ToArray()));
    }

    public void Dispose()
    {
        StopAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
