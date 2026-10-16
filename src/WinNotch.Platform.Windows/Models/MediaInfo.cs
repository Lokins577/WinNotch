using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WinNotch.Models;

public partial class MediaInfo : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _artist = string.Empty;

    [ObservableProperty]
    private string _albumTitle = string.Empty;

    [ObservableProperty]
    private BitmapImage? _albumArt;

    [ObservableProperty]
    private TimeSpan _position;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isShuffle;

    [ObservableProperty]
    private int _repeatMode; // 0=off, 1=all, 2=one

    [ObservableProperty]
    private System.Windows.Media.Color _dominantColor = System.Windows.Media.Colors.White;

    [ObservableProperty]
    private bool _hasMedia;

    public double ProgressPercent =>
        Duration.TotalSeconds > 0 ? Position.TotalSeconds / Duration.TotalSeconds : 0;

    partial void OnPositionChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(ProgressPercent));
    }

    partial void OnDurationChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(ProgressPercent));
    }
}
