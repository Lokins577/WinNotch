using System;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WinNotch.Services;

/// <summary>
/// Captures system audio output via WASAPI loopback and computes FFT
/// spectrum data for the visualizer bars.
/// </summary>
public class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly float[] _fftBuffer;
    private readonly int _fftSize;
    private int _fftPos;
    private bool _isCapturing;
    private bool _disposed;
    private readonly object _captureLock = new();

    /// <summary>
    /// Spectrum levels (0..1) for each frequency band.
    /// Updated on each FFT cycle. Thread-safe reads expected from UI.
    /// </summary>
    public float[] SpectrumData { get; }

    /// <summary>Number of output frequency bands.</summary>
    public int BandCount { get; }

    public event Action? SpectrumUpdated;

    public AudioCaptureService(int bandCount = 12, int fftSize = 2048)
    {
        BandCount = bandCount;
        _fftSize = fftSize;
        _fftBuffer = new float[fftSize];
        SpectrumData = new float[bandCount];
    }

    public void Start()
    {
        lock (_captureLock)
        {
            if (_isCapturing || _disposed) return;

            try
            {
                _capture = new WasapiLoopbackCapture();
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                _isCapturing = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioCapture start failed: {ex.Message}");
            }
        }
    }

    public void Stop()
    {
        lock (_captureLock)
        {
            if (!_isCapturing) return;
            _isCapturing = false;

            try
            {
                _capture?.StopRecording();
            }
            catch { }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_isCapturing || _capture == null) return;

        int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
        int channels = _capture.WaveFormat.Channels;
        int sampleCount = e.BytesRecorded / (bytesPerSample * channels);

        for (int i = 0; i < sampleCount; i++)
        {
            // Mix to mono: take first channel
            int offset = i * bytesPerSample * channels;
            float sample = bytesPerSample switch
            {
                4 => BitConverter.ToSingle(e.Buffer, offset), // IEEE float
                2 => BitConverter.ToInt16(e.Buffer, offset) / 32768f,
                _ => 0f
            };

            _fftBuffer[_fftPos++] = sample;

            if (_fftPos >= _fftSize)
            {
                _fftPos = 0;
                ProcessFft();
            }
        }
    }

    private void ProcessFft()
    {
        // Apply Hanning window
        var windowed = new NAudio.Dsp.Complex[_fftSize];
        for (int i = 0; i < _fftSize; i++)
        {
            float window = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (_fftSize - 1))));
            windowed[i].X = _fftBuffer[i] * window;
            windowed[i].Y = 0;
        }

        // FFT
        int m = (int)Math.Log2(_fftSize);
        NAudio.Dsp.FastFourierTransform.FFT(true, m, windowed);

        // Compute magnitude for each band (log frequency mapping)
        int halfN = _fftSize / 2;
        float maxFreqIdx = halfN;

        for (int band = 0; band < BandCount; band++)
        {
            // Log-scale frequency bands
            float lowFrac = (float)Math.Pow(band / (float)BandCount, 2.0);
            float highFrac = (float)Math.Pow((band + 1) / (float)BandCount, 2.0);

            int lowBin = Math.Max(1, (int)(lowFrac * maxFreqIdx));
            int highBin = Math.Min(halfN - 1, (int)(highFrac * maxFreqIdx));
            if (highBin <= lowBin) highBin = lowBin + 1;

            float sum = 0;
            for (int j = lowBin; j <= highBin; j++)
            {
                float mag = (float)Math.Sqrt(windowed[j].X * windowed[j].X + windowed[j].Y * windowed[j].Y);
                sum = Math.Max(sum, mag);
            }

            // Convert to dB-ish scale (0..1)
            float db = 20f * (float)Math.Log10(Math.Max(sum, 1e-10f));
            float normalized = Math.Clamp((db + 60f) / 60f, 0f, 1f);

            // Smooth with previous value
            SpectrumData[band] = SpectrumData[band] * 0.3f + normalized * 0.7f;
        }

        SpectrumUpdated?.Invoke();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Clear spectrum
        Array.Clear(SpectrumData, 0, SpectrumData.Length);
        SpectrumUpdated?.Invoke();

        // If capture stopped without Stop() being called (default device changed,
        // device unplugged), restart on the new default device after a short delay.
        bool unexpected;
        lock (_captureLock)
        {
            unexpected = _isCapturing && !_disposed;
            if (unexpected)
                _isCapturing = false;
        }

        if (unexpected)
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(1500);
                lock (_captureLock)
                {
                    if (_disposed || _isCapturing) return;
                    try
                    {
                        _capture?.Dispose();
                    }
                    catch { }
                    _capture = null;
                }
                Start();
            });
        }
    }

    public void Dispose()
    {
        lock (_captureLock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
        _capture?.Dispose();
    }
}
