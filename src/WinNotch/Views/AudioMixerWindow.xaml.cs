using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinNotch.AudioControl;
using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Settings;
using PlatformAudioCaptureService = WinNotch.Services.AudioCaptureService;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace WinNotch.Views;

public partial class AudioMixerWindow : Window
{
    private readonly AudioMixerFeatureService _service;
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _store;
    private readonly PlatformAudioCaptureService _audioCapture;
    private readonly DispatcherTimer _refreshTimer;
    private readonly List<WpfRectangle> _spectrumBars = [];
    private bool _refreshing;

    public AudioMixerWindow(
        AudioMixerFeatureService service,
        AppSettings settings,
        IAppSettingsStore store,
        PlatformAudioCaptureService audioCapture)
    {
        _service = service;
        _settings = settings;
        _store = store;
        _audioCapture = audioCapture;
        InitializeComponent();
        for (int index = 0; index < 12; index++)
        {
            var bar = new WpfRectangle
            {
                Margin = new Thickness(2),
                Fill = new SolidColorBrush(Color.FromRgb(89, 156, 255)),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 2
            };
            _spectrumBars.Add(bar);
            SpectrumHost.Children.Add(bar);
        }
        _service.ProcessSpectrumUpdated += OnProcessSpectrumUpdated;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _refreshTimer.Start(); };
    }

    private void Refresh()
    {
        try
        {
            var snapshot = _service.Refresh();
            _refreshing = true;
            var outputs = snapshot.Endpoints.Where(endpoint => endpoint.Kind == AudioEndpointKind.Output).ToArray();
            string? selectedId = _settings.AudioControlPreferences.PreferredVisualizerOutputId;
            OutputDeviceBox.ItemsSource = outputs;
            OutputDeviceBox.SelectedItem = outputs.FirstOrDefault(endpoint => endpoint.Id == selectedId)
                                           ?? outputs.FirstOrDefault(endpoint => endpoint.IsDefault);
            BuildSessions(snapshot.Sessions, snapshot.ProcessLoopback);
            BuildInputs(snapshot.Endpoints.Where(endpoint => endpoint.Kind == AudioEndpointKind.Input));
            StatusText.Text = $"Updated {snapshot.CapturedAt.ToLocalTime():T}. System routing changes open the Windows-owned settings surface.";
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
        finally { _refreshing = false; }
    }

    private void BuildSessions(IEnumerable<AudioSessionInfo> sessions, ProcessLoopbackCapability capability)
    {
        SessionHost.Children.Clear();
        foreach (var session in sessions)
        {
            var grid = RowGrid();
            grid.Children.Add(new TextBlock
            {
                Text = $"{session.Name}  {(session.Peak * 100):0}%",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var slider = new Slider { Minimum = 0, Maximum = 1, Value = session.Volume, Tag = session, Margin = new Thickness(8, 0, 8, 0) };
            slider.ValueChanged += OnSessionVolumeChanged;
            Grid.SetColumn(slider, 1); grid.Children.Add(slider);
            var mute = new CheckBox { Content = "Mute", IsChecked = session.IsMuted, Tag = session, VerticalAlignment = VerticalAlignment.Center };
            mute.Checked += OnSessionMuteChanged; mute.Unchecked += OnSessionMuteChanged;
            Grid.SetColumn(mute, 2); grid.Children.Add(mute);
            var analyze = new Button { Content = "Analyze", Tag = session, Padding = new Thickness(7, 2, 7, 2), IsEnabled = capability.Supported && session.ProcessId > 0 };
            analyze.Click += OnAnalyze;
            Grid.SetColumn(analyze, 3); grid.Children.Add(analyze);
            SessionHost.Children.Add(grid);
        }
        LoopbackStatus.Text = capability.Detail;
    }

    private void BuildInputs(IEnumerable<AudioEndpointInfo> inputs)
    {
        InputHost.Children.Clear();
        foreach (var input in inputs)
        {
            var grid = RowGrid();
            grid.Children.Add(new TextBlock { Text = input.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var slider = new Slider { Minimum = 0, Maximum = 1, Value = input.Volume, Tag = input, Margin = new Thickness(8, 0, 8, 0) };
            slider.ValueChanged += OnInputVolumeChanged;
            Grid.SetColumn(slider, 1); grid.Children.Add(slider);
            var mute = new CheckBox { Content = "Mute", IsChecked = input.IsMuted, Tag = input, VerticalAlignment = VerticalAlignment.Center };
            mute.Checked += OnInputMuteChanged; mute.Unchecked += OnInputMuteChanged;
            Grid.SetColumn(mute, 2); grid.Children.Add(mute);
            InputHost.Children.Add(grid);
        }
    }

    private static Grid RowGrid()
    {
        var grid = new Grid { Height = 38, Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        return grid;
    }

    private void OnSessionVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshing || sender is not Slider { Tag: AudioSessionInfo session }) return;
        _service.Backend.SetSessionVolume(session.Id, (float)e.NewValue);
    }

    private void OnSessionMuteChanged(object sender, RoutedEventArgs e)
    {
        if (_refreshing || sender is not CheckBox { Tag: AudioSessionInfo session } toggle) return;
        _service.Backend.SetSessionMute(session.Id, toggle.IsChecked == true);
    }

    private void OnInputVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshing || sender is not Slider { Tag: AudioEndpointInfo endpoint }) return;
        _service.Backend.SetInputVolume(endpoint.Id, (float)e.NewValue);
    }

    private void OnInputMuteChanged(object sender, RoutedEventArgs e)
    {
        if (_refreshing || sender is not CheckBox { Tag: AudioEndpointInfo endpoint } toggle) return;
        _service.Backend.SetInputMute(endpoint.Id, toggle.IsChecked == true);
    }

    private void OnOutputDeviceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || OutputDeviceBox.SelectedItem is not AudioEndpointInfo endpoint) return;
        var preferences = _settings.AudioControlPreferences;
        preferences.PreferredVisualizerOutputId = endpoint.Id;
        preferences.OutputDevicePriority.RemoveAll(id => id == endpoint.Id);
        preferences.OutputDevicePriority.Insert(0, endpoint.Id);
        preferences.Normalize();
        _store.Save(_settings);
        _audioCapture.SetOutputDevicePreferences(preferences.PreferredVisualizerOutputId, preferences.OutputDevicePriority);
    }

    private async void OnAnalyze(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AudioSessionInfo session }) return;
        try
        {
            await _service.StartProcessAnalysisAsync(session.ProcessId);
            LoopbackStatus.Text = $"Analyzing {session.Name} and its child processes. Audio is not saved.";
            StopAnalysisButton.Visibility = Visibility.Visible;
        }
        catch (Exception exception) { LoopbackStatus.Text = exception.Message; }
    }

    private async void OnStopAnalysis(object sender, RoutedEventArgs e)
    {
        await _service.StopProcessAnalysisAsync();
        StopAnalysisButton.Visibility = Visibility.Collapsed;
        foreach (var bar in _spectrumBars) bar.Height = 2;
    }

    private void OnProcessSpectrumUpdated(int processId, IReadOnlyList<float> bands)
        => Dispatcher.BeginInvoke(() =>
        {
            for (int index = 0; index < _spectrumBars.Count; index++)
                _spectrumBars[index].Height = 2 + 66 * (index < bands.Count ? bands[index] : 0);
        });

    private void OnSoundSettings(object sender, RoutedEventArgs e) => _service.Backend.OpenWindowsSoundSettings();
    private void OnRoutingSettings(object sender, RoutedEventArgs e) => _service.Backend.OpenWindowsAppRoutingSettings();

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        _service.ProcessSpectrumUpdated -= OnProcessSpectrumUpdated;
        _ = _service.StopProcessAnalysisAsync();
        base.OnClosed(e);
    }
}
