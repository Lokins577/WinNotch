using System;
using NAudio.CoreAudioApi;

namespace WinNotch.Services;

/// <summary>
/// Monitors and controls system volume using NAudio's WASAPI endpoint.
/// Fires VolumeChanged when the user changes volume (via keyboard, etc.).
/// </summary>
public class VolumeService : IDisposable
{
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private AudioEndpointVolume? _endpointVolume;

    /// <summary>Current volume level 0.0..1.0</summary>
    public float Volume => _endpointVolume?.MasterVolumeLevelScalar ?? 0f;

    /// <summary>Whether audio is muted</summary>
    public bool IsMuted => _endpointVolume?.Mute ?? false;

    /// <summary>Fired when volume or mute state changes. Args: (volume 0..1, isMuted)</summary>
    public event Action<float, bool>? VolumeChanged;

    public void Initialize()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _endpointVolume = _device.AudioEndpointVolume;

            _endpointVolume.OnVolumeNotification += OnVolumeNotification;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VolumeService init failed: {ex.Message}");
        }
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        VolumeChanged?.Invoke(data.MasterVolume, data.Muted);
    }

    public void SetVolume(float level)
    {
        if (_endpointVolume == null) return;
        _endpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);
    }

    public void ToggleMute()
    {
        if (_endpointVolume == null) return;
        _endpointVolume.Mute = !_endpointVolume.Mute;
    }

    public void Dispose()
    {
        if (_endpointVolume != null)
            _endpointVolume.OnVolumeNotification -= OnVolumeNotification;
        _device?.Dispose();
        _enumerator?.Dispose();
    }
}
