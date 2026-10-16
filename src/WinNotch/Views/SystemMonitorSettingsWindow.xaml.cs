using System.Windows;
using WinNotch.Models;
using WinNotch.Services;
using WinNotch.Settings;

namespace WinNotch.Views;

public partial class SystemMonitorSettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _store;
    private readonly SystemMonitorFeatureService _service;

    public SystemMonitorSettingsWindow(
        AppSettings settings,
        IAppSettingsStore store,
        SystemMonitorFeatureService service)
    {
        _settings = settings;
        _store = store;
        _service = service;
        InitializeComponent();
        var preferences = settings.SystemMonitorPreferences;
        IntervalSlider.Value = preferences.SampleIntervalSeconds;
        DurationSlider.Value = preferences.SustainedSeconds;
        CpuEnabled.IsChecked = preferences.CpuAlertEnabled;
        CpuSlider.Value = preferences.CpuAlertPercent;
        MemoryEnabled.IsChecked = preferences.MemoryAlertEnabled;
        MemorySlider.Value = preferences.MemoryAlertPercent;
        GpuEnabled.IsChecked = preferences.GpuAlertEnabled;
        GpuSlider.Value = preferences.GpuAlertPercent;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var preferences = _settings.SystemMonitorPreferences;
        preferences.SampleIntervalSeconds = (int)IntervalSlider.Value;
        preferences.SustainedSeconds = (int)DurationSlider.Value;
        preferences.CpuAlertEnabled = CpuEnabled.IsChecked == true;
        preferences.CpuAlertPercent = CpuSlider.Value;
        preferences.MemoryAlertEnabled = MemoryEnabled.IsChecked == true;
        preferences.MemoryAlertPercent = MemorySlider.Value;
        preferences.GpuAlertEnabled = GpuEnabled.IsChecked == true;
        preferences.GpuAlertPercent = GpuSlider.Value;
        preferences.Normalize();
        _store.Save(_settings);
        _service.ApplyPreferences();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
