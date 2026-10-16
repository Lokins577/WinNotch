using System.Windows;
using System.Windows.Controls;
using WinNotch.DisplayEnergy;
using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Settings;

namespace WinNotch.Views;

public partial class DisplayEnergyWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _store;
    private readonly DisplayEnergyFeatureService _service;

    public DisplayEnergyWindow(
        AppSettings settings,
        IAppSettingsStore store,
        DisplayEnergyFeatureService service)
    {
        _settings = settings;
        _store = store;
        _service = service;
        InitializeComponent();
        KeepAwakeBox.ItemsSource = Enum.GetValues<KeepAwakeMode>();
        BluetoothBox.ItemsSource = Enum.GetValues<BluetoothSleepPolicy>();
        LoadValues();
        Loaded += async (_, _) => await RefreshHardwareAsync();
    }

    private void LoadValues()
    {
        var value = _settings.DisplayEnergyPreferences;
        KeepAwakeBox.SelectedItem = value.KeepAwake;
        FullscreenCheck.IsChecked = value.KeepAwakeWhileFullscreen;
        RecordingCheck.IsChecked = value.KeepAwakeWhileRecording;
        StepText.Text = value.BrightnessStep.ToString();
        DimmingSlider.Value = value.SoftwareDimmingOpacity;
        BluetoothBox.SelectedItem = value.BluetoothPolicy;
        BluetoothStatusText.Text = _service.BluetoothStatus;
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshHardwareAsync();

    private async Task RefreshHardwareAsync()
    {
        MonitorHost.Children.Clear();
        IReadOnlyList<ExternalMonitorInfo> monitors;
        try { monitors = await Task.Run(_service.GetExternalMonitors); }
        catch (Exception exception)
        {
            MonitorHost.Children.Add(new TextBlock { Text = exception.Message });
            monitors = [];
        }
        foreach (var monitor in monitors)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 3, 0, 7) };
            var validated = new CheckBox
            {
                Content = $"{monitor.Description} · {(monitor.BrightnessSupported ? "brightness available" : "unsupported")}",
                IsChecked = monitor.UserValidated,
                Tag = monitor,
                IsEnabled = monitor.BrightnessSupported
            };
            validated.Checked += OnDdcValidated;
            validated.Unchecked += OnDdcValidated;
            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = monitor.MaximumBrightness == monitor.MinimumBrightness ? 0
                    : (monitor.CurrentBrightness - monitor.MinimumBrightness) * 100d
                      / (monitor.MaximumBrightness - monitor.MinimumBrightness),
                Tag = monitor,
                IsEnabled = monitor.UserValidated && monitor.BrightnessSupported
            };
            slider.ValueChanged += OnDdcBrightnessChanged;
            panel.Children.Add(validated);
            panel.Children.Add(slider);
            MonitorHost.Children.Add(panel);
        }
        var temperature = await Task.Run(_service.ReadTemperature);
        TemperatureText.Text = temperature.Supported
            ? $"{temperature.Provider}: {temperature.Value:0.0} {temperature.Unit} · {temperature.Detail}"
            : $"Temperature unsupported: {temperature.Detail}";
        FanText.Text = "Fan control unsupported: " + _service.FanCapability.Detail;
        BluetoothStatusText.Text = _service.BluetoothStatus;
    }

    private void OnDdcValidated(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: ExternalMonitorInfo monitor } toggle) return;
        var ids = _settings.DisplayEnergyPreferences.ValidatedDdcMonitorIds;
        if (toggle.IsChecked == true)
        {
            if (MessageBox.Show(
                    "Only continue if you have physically verified DDC/CI brightness works correctly on this exact monitor. Enable writes?",
                    "Validate DDC/CI monitor", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                toggle.IsChecked = false;
                return;
            }
            if (!ids.Contains(monitor.Id, StringComparer.Ordinal)) ids.Add(monitor.Id);
        }
        else ids.Remove(monitor.Id);
        _settings.DisplayEnergyPreferences.Normalize();
        _store.Save(_settings);
        _ = RefreshHardwareAsync();
    }

    private void OnDdcBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || sender is not Slider { Tag: ExternalMonitorInfo monitor } slider || !slider.IsMouseCaptureWithin) return;
        if (!_service.TrySetExternalBrightness(monitor.Id, (int)Math.Round(e.NewValue), out string message))
            BluetoothStatusText.Text = message;
    }

    private void OnBrightnessDown(object sender, RoutedEventArgs e) => _service.StepBrightness(-1);
    private void OnBrightnessUp(object sender, RoutedEventArgs e) => _service.StepBrightness(1);

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var value = _settings.DisplayEnergyPreferences;
        value.KeepAwake = KeepAwakeBox.SelectedItem is KeepAwakeMode keep ? keep : KeepAwakeMode.Off;
        value.KeepAwakeWhileFullscreen = FullscreenCheck.IsChecked == true;
        value.KeepAwakeWhileRecording = RecordingCheck.IsChecked == true;
        if (int.TryParse(StepText.Text, out int step)) value.BrightnessStep = step;
        value.SoftwareDimmingOpacity = DimmingSlider.Value;
        value.BluetoothPolicy = BluetoothBox.SelectedItem is BluetoothSleepPolicy policy ? policy : BluetoothSleepPolicy.None;
        value.Normalize();
        _store.Save(_settings);
        await _service.ApplyPreferencesAsync();
        BluetoothStatusText.Text = _service.BluetoothStatus;
    }
}
