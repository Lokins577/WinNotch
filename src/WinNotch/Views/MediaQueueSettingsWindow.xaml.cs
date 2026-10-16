using System.Windows;
using WinNotch.Models;
using WinNotch.Music;
using WinNotch.Services;
using WinNotch.Settings;

namespace WinNotch.Views;

public partial class MediaQueueSettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IMediaCredentialStore _credentials;
    private readonly MediaQueueService _queue;
    private readonly Action _settingsChanged;

    public MediaQueueSettingsWindow(
        AppSettings settings,
        IAppSettingsStore settingsStore,
        IMediaCredentialStore credentials,
        MediaQueueService queue,
        Action settingsChanged)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _credentials = credentials;
        _queue = queue;
        _settingsChanged = settingsChanged;
        InitializeComponent();
        LoadValues();
    }

    private void LoadValues()
    {
        var preferences = _settings.MediaQueuePreferences;
        preferences.Normalize();
        SpotifyEnabledCheck.IsChecked = preferences.SpotifyEnabled;
        VlcEnabledCheck.IsChecked = preferences.VlcEnabled;
        VlcUrlText.Text = preferences.VlcBaseUrl;
        RefreshCredentialStatus();
        StatusText.Text = _queue.Snapshot.Message ?? string.Empty;
    }

    private void RefreshCredentialStatus()
    {
        SpotifyCredentialStatus.Text = HasCredential(MediaCredentialKeys.SpotifyAccessToken)
            ? "A token is saved. Leave the field blank to keep it."
            : "No token is saved.";
        VlcCredentialStatus.Text = HasCredential(MediaCredentialKeys.VlcPassword)
            ? "A password is saved. Leave the field blank to keep it."
            : "No password is saved.";
    }

    private bool HasCredential(string key)
    {
        try { return !string.IsNullOrEmpty(_credentials.Read(key)); }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
            return false;
        }
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var preferences = _settings.MediaQueuePreferences;
        preferences.SpotifyEnabled = SpotifyEnabledCheck.IsChecked == true;
        preferences.VlcEnabled = VlcEnabledCheck.IsChecked == true;
        preferences.VlcBaseUrl = VlcUrlText.Text.Trim();
        if (preferences.VlcEnabled
            && !MediaQueuePreferences.TryGetLoopbackBaseUri(preferences.VlcBaseUrl, out _))
        {
            StatusText.Text = "VLC URL must be an HTTP(S) loopback address, such as http://127.0.0.1:8080/.";
            return;
        }

        try
        {
            preferences.Normalize();
            if (!string.IsNullOrWhiteSpace(SpotifyTokenBox.Password))
                _credentials.Write(MediaCredentialKeys.SpotifyAccessToken, SpotifyTokenBox.Password.Trim());
            if (!string.IsNullOrEmpty(VlcPasswordBox.Password))
                _credentials.Write(MediaCredentialKeys.VlcPassword, VlcPasswordBox.Password);
            SpotifyTokenBox.Clear();
            VlcPasswordBox.Clear();
            _settingsStore.Save(_settings);
            _settingsChanged();
            _queue.Reconfigure();
            await _queue.RefreshAsync();
            StatusText.Text = _queue.Snapshot.Message
                              ?? $"{_queue.Snapshot.Provider} queue: {_queue.Snapshot.Queue.Count} item(s).";
            RefreshCredentialStatus();
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void OnClearSpotify(object sender, RoutedEventArgs e)
    {
        ClearCredential(MediaCredentialKeys.SpotifyAccessToken);
    }

    private void OnClearVlc(object sender, RoutedEventArgs e)
    {
        ClearCredential(MediaCredentialKeys.VlcPassword);
    }

    private void ClearCredential(string key)
    {
        try
        {
            _credentials.Delete(key);
            _queue.Reconfigure();
            RefreshCredentialStatus();
            StatusText.Text = "Saved credential removed.";
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
