using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using WinNotch.MediaTools;
using WinNotch.Platform.Windows.Capture;
using WinNotch.Capture;

namespace WinNotch.Platform.Windows.MediaTools;

public sealed class ScreenRecordingService : IDisposable
{
    private readonly WindowsCaptureService _capture;
    private readonly Func<Rectangle, Bitmap> _captureRegion;
    private readonly string _storageRoot;
    private readonly object _audioSync = new();
    private CancellationTokenSource? _cancellation;
    private Task? _recordTask;
    private Stopwatch? _clock;
    private WasapiLoopbackCapture? _systemCapture;
    private WasapiCapture? _microphoneCapture;
    private WaveFileWriter? _systemWriter;
    private WaveFileWriter? _microphoneWriter;
    private bool _disposed;

    public ScreenRecordingService(
        WindowsCaptureService capture,
        string? storageRoot = null,
        Func<Rectangle, Bitmap>? captureRegion = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _captureRegion = captureRegion ?? capture.CaptureRegion;
        _storageRoot = Path.GetFullPath(storageRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinNotch", "Recordings"));
    }

    public bool IsRecording => _recordTask is { IsCompleted: false };
    public RecordingProject? CurrentProject { get; private set; }
    public event Action<RecordingFrame>? FrameCaptured;
    public event Action<RecordingProject>? RecordingStopped;

    public void Start(Rectangle bounds, RecordingPreferences preferences)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(preferences);
        if (IsRecording) throw new InvalidOperationException("A recording is already running.");
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        preferences.Normalize();

        var id = Guid.NewGuid();
        string directory = Path.Combine(_storageRoot, id.ToString("N"));
        string framesDirectory = Path.Combine(directory, "frames");
        Directory.CreateDirectory(framesDirectory);
        var project = new RecordingProject
        {
            Id = id,
            Directory = directory,
            StartedAt = DateTimeOffset.UtcNow,
            Width = bounds.Width,
            Height = bounds.Height,
            FramesPerSecond = preferences.FramesPerSecond
        };
        CurrentProject = project;
        StartAudio(project, preferences);
        _cancellation = new CancellationTokenSource();
        _clock = Stopwatch.StartNew();
        _recordTask = Task.Run(() => CaptureLoopAsync(
            project,
            framesDirectory,
            bounds,
            preferences,
            _cancellation.Token));
    }

    public async Task<RecordingProject> StopAsync()
    {
        var project = CurrentProject
                      ?? throw new InvalidOperationException("No recording project exists.");
        _cancellation?.Cancel();
        if (_recordTask != null)
        {
            try { await _recordTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        FinalizeProject(project);
        return project;
    }

    private async Task CaptureLoopAsync(
        RecordingProject project,
        string framesDirectory,
        Rectangle bounds,
        RecordingPreferences preferences,
        CancellationToken cancellationToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(1d / preferences.FramesPerSecond);
        TimeSpan maximum = TimeSpan.FromMinutes(preferences.MaximumMinutes);
        int index = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested && _clock!.Elapsed < maximum)
            {
                TimeSpan timestamp = _clock.Elapsed;
                using var frame = _captureRegion(bounds);
                string path = Path.Combine(framesDirectory, $"{index:D7}.jpg");
                _capture.Save(frame, path, CaptureImageFormat.Jpeg, preferences.FrameJpegQuality);
                var record = new RecordingFrame(index++, timestamp, path);
                lock (project.Frames) project.Frames.Add(record);
                PublishFrame(record);
                TimeSpan delay = interval * index - _clock.Elapsed;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            StopAudio();
            project.Duration = _clock?.Elapsed ?? TimeSpan.Zero;
            SaveManifest(project);
            PublishStopped(project);
        }
    }

    private void StartAudio(RecordingProject project, RecordingPreferences preferences)
    {
        if (preferences.CaptureSystemAudio)
        {
            try
            {
                string path = Path.Combine(project.Directory, "system.wav");
                _systemCapture = new WasapiLoopbackCapture();
                _systemWriter = new WaveFileWriter(path, _systemCapture.WaveFormat);
                _systemCapture.DataAvailable += OnSystemAudio;
                _systemCapture.StartRecording();
                project.SystemAudioPath = path;
            }
            catch (Exception exception)
            {
                project.Warnings.Add($"System audio unavailable: {exception.Message}");
                DisposeSystemAudio();
            }
        }
        if (preferences.CaptureMicrophone)
        {
            try
            {
                string path = Path.Combine(project.Directory, "microphone.wav");
                _microphoneCapture = new WasapiCapture();
                _microphoneWriter = new WaveFileWriter(path, _microphoneCapture.WaveFormat);
                _microphoneCapture.DataAvailable += OnMicrophoneAudio;
                _microphoneCapture.StartRecording();
                project.MicrophonePath = path;
            }
            catch (Exception exception)
            {
                project.Warnings.Add($"Microphone unavailable: {exception.Message}");
                DisposeMicrophoneAudio();
            }
        }
    }

    private void OnSystemAudio(object? sender, WaveInEventArgs e)
    {
        lock (_audioSync) _systemWriter?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnMicrophoneAudio(object? sender, WaveInEventArgs e)
    {
        lock (_audioSync) _microphoneWriter?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private void StopAudio()
    {
        WasapiLoopbackCapture? systemCapture;
        WasapiCapture? microphoneCapture;
        WaveFileWriter? systemWriter;
        WaveFileWriter? microphoneWriter;
        lock (_audioSync)
        {
            systemCapture = _systemCapture;
            microphoneCapture = _microphoneCapture;
            systemWriter = _systemWriter;
            microphoneWriter = _microphoneWriter;
            if (systemCapture != null) systemCapture.DataAvailable -= OnSystemAudio;
            if (microphoneCapture != null) microphoneCapture.DataAvailable -= OnMicrophoneAudio;
            _systemCapture = null;
            _microphoneCapture = null;
            _systemWriter = null;
            _microphoneWriter = null;
        }
        try { systemCapture?.StopRecording(); } catch { }
        try { microphoneCapture?.StopRecording(); } catch { }
        systemCapture?.Dispose();
        microphoneCapture?.Dispose();
        systemWriter?.Dispose();
        microphoneWriter?.Dispose();
    }

    private void DisposeSystemAudio()
    {
        if (_systemCapture != null) _systemCapture.DataAvailable -= OnSystemAudio;
        _systemCapture?.Dispose();
        _systemCapture = null;
        _systemWriter?.Dispose();
        _systemWriter = null;
    }

    private void DisposeMicrophoneAudio()
    {
        if (_microphoneCapture != null) _microphoneCapture.DataAvailable -= OnMicrophoneAudio;
        _microphoneCapture?.Dispose();
        _microphoneCapture = null;
        _microphoneWriter?.Dispose();
        _microphoneWriter = null;
    }

    private void FinalizeProject(RecordingProject project)
    {
        _clock?.Stop();
        project.Duration = _clock?.Elapsed ?? project.Duration;
        StopAudio();
        SaveManifest(project);
        _cancellation?.Dispose();
        _cancellation = null;
        _recordTask = null;
        _clock = null;
    }

    private static void SaveManifest(RecordingProject project)
    {
        string temporary = Path.Combine(project.Directory, "project.json.tmp");
        string destination = Path.Combine(project.Directory, "project.json");
        File.WriteAllText(temporary, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, destination, overwrite: true);
    }

    private void PublishFrame(RecordingFrame frame)
    {
        foreach (Action<RecordingFrame> handler in FrameCaptured?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try { handler(frame); } catch { }
        }
    }

    private void PublishStopped(RecordingProject project)
    {
        foreach (Action<RecordingProject> handler in RecordingStopped?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try { handler(project); } catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _cancellation?.Cancel();
        try { _recordTask?.GetAwaiter().GetResult(); } catch { }
        if (CurrentProject != null) FinalizeProject(CurrentProject);
        _disposed = true;
    }
}
