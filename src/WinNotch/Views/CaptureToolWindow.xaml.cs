using System.Drawing;
using System.Windows;
using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Settings;
using WinNotch.WindowManagement;
using Forms = System.Windows.Forms;

namespace WinNotch.Views;

public partial class CaptureToolWindow : Window
{
    private readonly CaptureFeatureService _service;
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _store;
    private readonly WindowsWindowManagementBackend _windows = new();

    public CaptureToolWindow(
        CaptureFeatureService service,
        AppSettings settings,
        IAppSettingsStore store)
    {
        _service = service;
        _settings = settings;
        _store = store;
        InitializeComponent();
        RefreshWindows();
    }

    private ManagedWindow? SelectedWindow => WindowBox.SelectedItem as ManagedWindow;

    private void RefreshWindows()
    {
        try
        {
            WindowBox.ItemsSource = _windows.GetWindows();
            WindowBox.SelectedIndex = WindowBox.Items.Count > 0 ? 0 : -1;
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void OnRefreshWindows(object sender, RoutedEventArgs e) => RefreshWindows();

    private async void OnRegion(object sender, RoutedEventArgs e)
    {
        Hide();
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var overlay = new RegionCaptureOverlay(screen);
        if (overlay.ShowDialog() == true && overlay.SelectedRegion is { } region)
        {
            await Task.Delay(80);
            OpenEditor(_service.Capture.CaptureRegion(region));
        }
        else Show();
    }

    private async void OnDisplay(object sender, RoutedEventArgs e)
    {
        Hide();
        Forms.Screen screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        await Task.Delay(80);
        OpenEditor(_service.Capture.CaptureRegion(screen.Bounds));
    }

    private async void OnWindow(object sender, RoutedEventArgs e)
    {
        var selected = SelectedWindow;
        if (selected == null) return;
        Hide();
        try { OpenEditor(await _service.Capture.CaptureWindowAsync(new IntPtr(selected.Handle))); }
        catch (Exception exception) { ShowFailure(exception); }
    }

    private async void OnScrolling(object sender, RoutedEventArgs e)
    {
        var selected = SelectedWindow;
        if (selected == null) return;
        Hide();
        try
        {
            _windows.Activate(selected.Handle);
            await Task.Delay(250);
            OpenEditor(await _service.Scrolling.CaptureAsync(new IntPtr(selected.Handle)));
        }
        catch (Exception exception) { ShowFailure(exception); }
    }

    private async void OnColor(object sender, RoutedEventArgs e)
    {
        Hide();
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var picker = new PointPickerOverlay(screen);
        if (picker.ShowDialog() == true && picker.SelectedPoint is { } point)
        {
            await Task.Delay(80);
            var color = _service.Capture.SampleColor(point);
            string value = $"{color.Hex} · {color.Rgb} · {color.Hsl} · {color.Hsv}";
            System.Windows.Clipboard.SetText(value);
            Show();
            StatusText.Text = value + " · copied";
        }
        else Show();
    }

    private void OpenEditor(Bitmap bitmap)
    {
        try
        {
            var owner = Owner;
            var editor = new CaptureEditorWindow(
                bitmap, _service.Capture, _service.Recognition, _settings, _store);
            if (owner?.IsVisible == true) editor.Owner = owner;
            editor.Show();
            Close();
        }
        finally { bitmap.Dispose(); }
    }

    private void ShowFailure(Exception exception)
    {
        Show();
        Activate();
        StatusText.Text = exception.Message;
    }

    protected override void OnClosed(EventArgs e)
    {
        _windows.Dispose();
        base.OnClosed(e);
    }
}
