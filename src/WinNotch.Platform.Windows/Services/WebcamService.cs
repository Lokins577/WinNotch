using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Graphics.Imaging;
using Debug = System.Diagnostics.Debug;

namespace WinNotch.Services;

public class WebcamService : IDisposable
{
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private bool _isRunning;
    private bool _disposed;

    public event Action<ImageSource>? FrameReady;
    public bool IsAvailable { get; private set; }
    public double MaxCameraFps { get; private set; }

    private double _targetFps = 15;
    private long _lastFrameTicks;
    private byte[]? _frameBuffer;
    private int _uiFramePending;

    public void SetTargetFps(int fps)
    {
        _targetFps = Math.Clamp(fps, 5, MaxCameraFps > 0 ? MaxCameraFps : 60);
    }

    public async Task StartAsync()
    {
        if (_isRunning) return;

        try
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            Debug.WriteLine($"[Webcam] Found {groups.Count} camera groups");

            MediaFrameSourceGroup? selectedGroup = null;
            MediaFrameSourceInfo? selectedInfo = null;

            // Score-based camera selection: higher = preferred
            int bestScore = -1;

            foreach (var group in groups)
            {
                Debug.WriteLine($"[Webcam] Group: '{group.DisplayName}' ({group.Id})");

                foreach (var info in group.SourceInfos)
                {
                    if (info.MediaStreamType != Windows.Media.Capture.MediaStreamType.VideoPreview &&
                        info.MediaStreamType != Windows.Media.Capture.MediaStreamType.VideoRecord)
                        continue;
                    if (info.SourceKind != MediaFrameSourceKind.Color)
                        continue;

                    var name = (group.DisplayName ?? "").ToLowerInvariant();
                    Debug.WriteLine($"[Webcam]   Source: '{name}' kind={info.SourceKind} stream={info.MediaStreamType}");

                    int score = 10; // base score for any valid camera

                    // Penalize phone/virtual cameras heavily
                    if (name.Contains("phone") || name.Contains("virtual") ||
                        name.Contains("droidcam") || name.Contains("ivcam") ||
                        name.Contains("epoccam") || name.Contains("iriun") ||
                        name.Contains("camo"))
                    {
                        score = 1;
                    }
                    // Boost integrated/built-in cameras
                    else if (name.Contains("integrated") || name.Contains("built-in") ||
                             name.Contains("webcam") || name.Contains("camera"))
                    {
                        score = 20;
                    }

                    Debug.WriteLine($"[Webcam]   Score: {score} (current best: {bestScore})");

                    if (score > bestScore)
                    {
                        bestScore = score;
                        selectedGroup = group;
                        selectedInfo = info;
                    }
                }
            }

            if (selectedGroup == null || selectedInfo == null)
            {
                Debug.WriteLine("[Webcam] No suitable camera found");
                IsAvailable = false;
                return;
            }

            Debug.WriteLine($"[Webcam] Selected: '{selectedGroup.DisplayName}'");

            _capture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                SourceGroup = selectedGroup,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video
            };

            await _capture.InitializeAsync(settings);
            Debug.WriteLine("[Webcam] MediaCapture initialized");

            var source = _capture.FrameSources[selectedInfo.Id];

            // Pick a small resolution format to minimize CPU usage
            MediaFrameFormat? bestFormat = null;
            foreach (var fmt in source.SupportedFormats)
            {
                if (fmt.VideoFormat.Width <= 640 && fmt.VideoFormat.Width >= 160)
                {
                    if (bestFormat == null || fmt.VideoFormat.Width > bestFormat.VideoFormat.Width)
                        bestFormat = fmt;
                }
            }
            if (bestFormat != null)
                await source.SetFormatAsync(bestFormat);

            // Detect max camera fps from the selected format
            var currentFormat = source.CurrentFormat;
            if (currentFormat?.FrameRate != null && currentFormat.FrameRate.Denominator > 0)
            {
                MaxCameraFps = (double)currentFormat.FrameRate.Numerator / currentFormat.FrameRate.Denominator;
                Debug.WriteLine($"[Webcam] Max camera FPS: {MaxCameraFps:F1}");
            }
            else
            {
                MaxCameraFps = 30;
            }

            _reader = await _capture.CreateFrameReaderAsync(source);
            _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            _reader.FrameArrived += OnFrameArrived;

            var status = await _reader.StartAsync();
            _isRunning = status == MediaFrameReaderStartStatus.Success;
            IsAvailable = _isRunning;
            Debug.WriteLine($"[Webcam] Reader start status: {status}, IsAvailable={IsAvailable}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Webcam] Start failed: {ex.GetType().Name}: {ex.Message}");
            IsAvailable = false;
        }
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        // Time-based throttle to target FPS
        long now = Environment.TickCount64;
        long minInterval = (long)(1000.0 / _targetFps);
        long last = Interlocked.Read(ref _lastFrameTicks);
        if (now - last < minInterval) return;
        Interlocked.Exchange(ref _lastFrameTicks, now);

        // Skip this frame if the UI thread hasn't consumed the previous one —
        // this also lets us reuse a single pixel buffer (no per-frame ~1 MB alloc)
        if (Interlocked.CompareExchange(ref _uiFramePending, 1, 0) != 0) return;

        bool dispatched = false;
        try
        {
            using var frameRef = sender.TryAcquireLatestFrame();
            var bitmap = frameRef?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap == null) return;

            // Convert to Bgra8 if needed
            SoftwareBitmap converted;
            if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                bitmap.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
            {
                converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            }
            else
            {
                converted = SoftwareBitmap.Copy(bitmap);
            }

            int w = converted.PixelWidth;
            int h = converted.PixelHeight;
            int byteCount = w * h * 4;
            if (_frameBuffer == null || _frameBuffer.Length != byteCount)
                _frameBuffer = new byte[byteCount];
            var buffer = _frameBuffer;

            converted.CopyToBuffer(buffer.AsBuffer());
            converted.Dispose();

            // Create WPF ImageSource on the UI thread (BitmapSource.Create copies
            // the buffer synchronously, after which it is safe to reuse)
            dispatched = System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                try
                {
                    var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, buffer, w * 4);
                    bmp.Freeze();
                    FrameReady?.Invoke(bmp);
                }
                catch { }
                finally
                {
                    Interlocked.Exchange(ref _uiFramePending, 0);
                }
            }) != null;
        }
        finally
        {
            if (!dispatched)
                Interlocked.Exchange(ref _uiFramePending, 0);
        }
    }

    public async Task StopAsync()
    {
        if (!_isRunning) return;
        _isRunning = false;

        if (_reader != null)
        {
            _reader.FrameArrived -= OnFrameArrived;
            await _reader.StopAsync();
            _reader.Dispose();
            _reader = null;
        }

        _capture?.Dispose();
        _capture = null;
        IsAvailable = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = StopAsync();
    }
}
