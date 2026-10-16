using System.Drawing;
using System.Windows;
using WinNotch.MediaTools;
using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Settings;
using Forms = System.Windows.Forms;

namespace WinNotch.Views;

public partial class RecordingToolWindow : Window
{
    private readonly MediaToolsFeatureService _service;
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _store;
    private bool _manualStop;
    private int _frames;

    public RecordingToolWindow(
        MediaToolsFeatureService service,
        AppSettings settings,
        IAppSettingsStore store)
    {
        _service = service;
        _settings = settings;
        _store = store;
        InitializeComponent();
        ScreenBox.ItemsSource = Forms.Screen.AllScreens;
        ScreenBox.SelectedItem = Forms.Screen.PrimaryScreen;
        var preferences = settings.RecordingPreferences;
        FpsSlider.Value = preferences.FramesPerSecond;
        SystemAudioCheck.IsChecked = preferences.CaptureSystemAudio;
        MicrophoneCheck.IsChecked = preferences.CaptureMicrophone;
        _service.Recorder.FrameCaptured += OnFrameCaptured;
        _service.Recorder.RecordingStopped += OnRecordingStopped;
        SetRecordingState(_service.Recorder.IsRecording);
    }

    private void OnStartDisplay(object sender, RoutedEventArgs e)
    {
        if (ScreenBox.SelectedItem is Forms.Screen screen) Start(screen.Bounds);
    }

    private async void OnStartRegion(object sender, RoutedEventArgs e)
    {
        Hide();
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var overlay = new RegionCaptureOverlay(screen);
        if (overlay.ShowDialog() == true && overlay.SelectedRegion is { } region)
        {
            await Task.Delay(80);
            Show();
            Start(region);
        }
        else Show();
    }

    private void Start(Rectangle bounds)
    {
        try
        {
            var preferences = _settings.RecordingPreferences;
            preferences.FramesPerSecond = (int)FpsSlider.Value;
            preferences.CaptureSystemAudio = SystemAudioCheck.IsChecked == true;
            preferences.CaptureMicrophone = MicrophoneCheck.IsChecked == true;
            preferences.Normalize();
            _store.Save(_settings);
            _frames = 0;
            _service.Recorder.Start(bounds, preferences);
            SetRecordingState(true);
            StatusText.Text = $"Recording {bounds.Width}×{bounds.Height} at {preferences.FramesPerSecond} fps.";
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private async void OnStop(object sender, RoutedEventArgs e)
    {
        try
        {
            _manualStop = true;
            var project = await _service.Recorder.StopAsync();
            SetRecordingState(false);
            OpenEditor(project);
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
        finally { _manualStop = false; }
    }

    private void OnFrameCaptured(RecordingFrame frame)
        => Dispatcher.BeginInvoke(() =>
        {
            _frames = frame.Index + 1;
            StatusText.Text = $"Recording… {_frames} frames · {frame.Timestamp:mm\\:ss}";
        });

    private void OnRecordingStopped(RecordingProject project)
        => Dispatcher.BeginInvoke(() =>
        {
            SetRecordingState(false);
            if (!_manualStop) OpenEditor(project);
        });

    private void OpenEditor(RecordingProject project)
    {
        var editor = new RecordingEditorWindow(project, _service.Exporter) { Owner = this };
        editor.Show();
        editor.Activate();
        StatusText.Text = $"Recorded {project.Frames.Count} frames. {string.Join(" ", project.Warnings)}";
    }

    private void OnImageTools(object sender, RoutedEventArgs e)
    {
        var window = new ImageToolsWindow(_service.Exporter) { Owner = this };
        window.ShowDialog();
    }

    private void SetRecordingState(bool recording)
    {
        StartDisplayButton.IsEnabled = StartRegionButton.IsEnabled = !recording;
        StopButton.IsEnabled = recording;
        RecordingProgress.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnClosed(EventArgs e)
    {
        _service.Recorder.FrameCaptured -= OnFrameCaptured;
        _service.Recorder.RecordingStopped -= OnRecordingStopped;
        base.OnClosed(e);
    }
}
