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
    private const int WmHotKey = 0x0312;
    private const int PlainTextHotKeyId = 0x574E4350;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const byte VkControl = 0x11;
    private const byte VkV = 0x56;
    private const uint KeyUp = 0x0002;
    private readonly ClipboardHistory _history;
    private readonly IClipboardHistoryStore _store;
    private readonly ClipboardPreferences _preferences;
    private HwndSource? _source;
    private uint _ignoredSequence;
    private System.Threading.Timer? _autoClearTimer;
    private uint _autoClearSequence;
    private bool _hotKeyRegistered;

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
        ApplyHotKeyRegistration();
        Publish();
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (_source == null) return ValueTask.CompletedTask;
        RemoveClipboardFormatListener(_source.Handle);
        if (_hotKeyRegistered)
        {
            UnregisterHotKey(_source.Handle, PlainTextHotKeyId);
            _hotKeyRegistered = false;
        }
        _autoClearTimer?.Dispose();
        _autoClearTimer = null;
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

    public void CopyPlainText(ClipboardHistoryItem item)
    {
        if (item.Kind != ClipboardItemKind.Text || item.Text == null) return;
        System.Windows.Clipboard.SetText(item.Text);
        _ignoredSequence = GetClipboardSequenceNumber();
    }

    public void CopyCleanUrl(ClipboardHistoryItem item)
    {
        if (item.Kind != ClipboardItemKind.Text || item.Text == null) return;
        string cleaned = UrlCleaner.Clean(item.Text);
        System.Windows.Clipboard.SetText(cleaned);
        _ignoredSequence = GetClipboardSequenceNumber();
    }

    public void ApplyPreferences()
    {
        _preferences.Normalize();
        ApplyHotKeyRegistration();
        if (_preferences.AutoClearSeconds == 0)
        {
            _autoClearTimer?.Dispose();
            _autoClearTimer = null;
        }
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
        else if (message == WmHotKey && wParam.ToInt32() == PlainTextHotKeyId)
        {
            handled = true;
            _ = PastePlainTextAsync();
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
                if (item != null)
                {
                    if (item.Kind == ClipboardItemKind.Text && item.Text != null && _preferences.AutoCleanUrls)
                    {
                        string cleaned = UrlCleaner.Clean(item.Text);
                        if (!string.Equals(cleaned, item.Text, StringComparison.Ordinal))
                        {
                            item.Text = cleaned;
                            System.Windows.Clipboard.SetText(cleaned);
                            _ignoredSequence = GetClipboardSequenceNumber();
                        }
                    }
                    if (_history.Add(item)) PersistAndPublish();
                    ScheduleAutoClear(GetClipboardSequenceNumber());
                }
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

    private void ScheduleAutoClear(uint sequence)
    {
        if (_preferences.AutoClearSeconds <= 0 || sequence == 0) return;
        _autoClearSequence = sequence;
        _autoClearTimer?.Dispose();
        _autoClearTimer = new System.Threading.Timer(_ =>
        {
            _source?.Dispatcher.BeginInvoke(() =>
            {
                if (_autoClearSequence == 0 || GetClipboardSequenceNumber() != _autoClearSequence)
                    return;
                try
                {
                    System.Windows.Clipboard.Clear();
                    _ignoredSequence = GetClipboardSequenceNumber();
                    _autoClearSequence = 0;
                }
                catch { }
            });
        }, null, TimeSpan.FromSeconds(_preferences.AutoClearSeconds), Timeout.InfiniteTimeSpan);
    }

    private async Task PastePlainTextAsync()
    {
        if (!_preferences.PlainTextPasteHotkey) return;
        try
        {
            var original = CloneClipboardData(System.Windows.Clipboard.GetDataObject());
            if (!System.Windows.Clipboard.ContainsText(System.Windows.TextDataFormat.UnicodeText)) return;
            string text = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText);
            System.Windows.Clipboard.SetText(text);
            _ignoredSequence = GetClipboardSequenceNumber();
            uint plainSequence = _ignoredSequence;

            keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            keybd_event(VkV, 0, 0, UIntPtr.Zero);
            keybd_event(VkV, 0, KeyUp, UIntPtr.Zero);
            keybd_event(VkControl, 0, KeyUp, UIntPtr.Zero);

            await Task.Delay(120);
            if (original != null && GetClipboardSequenceNumber() == plainSequence)
            {
                System.Windows.Clipboard.SetDataObject(original, true);
                _ignoredSequence = GetClipboardSequenceNumber();
            }
        }
        catch { }
    }

    private static System.Windows.DataObject? CloneClipboardData(System.Windows.IDataObject? source)
    {
        if (source == null) return null;
        var clone = new System.Windows.DataObject();
        foreach (string format in source.GetFormats(autoConvert: false))
        {
            try
            {
                var data = source.GetData(format, autoConvert: false);
                if (data != null) clone.SetData(format, data);
            }
            catch { }
        }
        return clone;
    }

    private void ApplyHotKeyRegistration()
    {
        if (_source == null) return;
        if (_hotKeyRegistered)
        {
            UnregisterHotKey(_source.Handle, PlainTextHotKeyId);
            _hotKeyRegistered = false;
        }
        if (_preferences.PlainTextPasteHotkey)
            _hotKeyRegistered = RegisterHotKey(
                _source.Handle, PlainTextHotKeyId, ModControl | ModShift, VkV);
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
