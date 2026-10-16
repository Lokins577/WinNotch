using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wasapi.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace WinNotch.Platform.Windows.AudioControl;

public sealed class ProcessLoopbackAnalyzer : IDisposable
{
    private const string ProcessLoopbackDevice = "VAD\\Process_Loopback";
    private const ushort VtBlob = 65;
    private readonly object _sync = new();
    private readonly float[] _fftBuffer;
    private readonly int _fftSize;
    private readonly int _bandCount;
    private AudioClient? _audioClient;
    private AudioCaptureClient? _captureClient;
    private CancellationTokenSource? _captureCancellation;
    private Task? _captureTask;
    private int _fftPosition;
    private bool _disposed;

    public ProcessLoopbackAnalyzer(int bandCount = 12, int fftSize = 2048)
    {
        if (bandCount <= 0) throw new ArgumentOutOfRangeException(nameof(bandCount));
        if (fftSize <= 0 || (fftSize & (fftSize - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(fftSize), "FFT size must be a power of two.");
        _bandCount = bandCount;
        _fftSize = fftSize;
        _fftBuffer = new float[fftSize];
    }

    public int? ActiveProcessId { get; private set; }
    public event Action<int, IReadOnlyList<float>>? SpectrumUpdated;

    public async Task StartAsync(int processId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        if (Environment.OSVersion.Version.Build < 20348)
            throw new PlatformNotSupportedException("Process loopback requires Windows build 20348 or later.");
        cancellationToken.ThrowIfCancellationRequested();
        await StopAsync().ConfigureAwait(false);

        var clientInterface = await ActivateAsync((uint)processId).ConfigureAwait(false);
        var client = new AudioClient(clientInterface);
        try
        {
            var format = new WaveFormat(44_100, 16, 2);
            client.Initialize(
                AudioClientShareMode.Shared,
                AudioClientStreamFlags.Loopback | AudioClientStreamFlags.AutoConvertPcm,
                0,
                0,
                format,
                Guid.Empty);
            var captureClient = client.AudioCaptureClient;
            client.Start();
            lock (_sync)
            {
                _audioClient = client;
                _captureClient = captureClient;
                _captureCancellation = new CancellationTokenSource();
                ActiveProcessId = processId;
                _fftPosition = 0;
                _captureTask = CaptureAsync(processId, format, _captureCancellation.Token);
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task StopAsync()
    {
        Task? task;
        lock (_sync)
        {
            _captureCancellation?.Cancel();
            task = _captureTask;
            _captureTask = null;
        }
        if (task != null)
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        lock (_sync)
        {
            try { _audioClient?.Stop(); }
            catch { }
            _captureClient?.Dispose();
            _audioClient?.Dispose();
            _captureClient = null;
            _audioClient = null;
            _captureCancellation?.Dispose();
            _captureCancellation = null;
            ActiveProcessId = null;
            Array.Clear(_fftBuffer);
        }
    }

    private async Task CaptureAsync(int processId, WaveFormat format, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                AudioCaptureClient? capture;
                lock (_sync) capture = _captureClient;
                if (capture == null) return;
                int packetSize = capture.GetNextPacketSize();
                while (packetSize > 0)
                {
                    IntPtr buffer = capture.GetBuffer(out int frames, out var flags);
                    try
                    {
                        ReadFrames(buffer, frames, flags, format, processId);
                    }
                    finally
                    {
                        capture.ReleaseBuffer(frames);
                    }
                    packetSize = capture.GetNextPacketSize();
                }
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { }
    }

    private void ReadFrames(
        IntPtr buffer,
        int frames,
        AudioClientBufferFlags flags,
        WaveFormat format,
        int processId)
    {
        int channels = Math.Max(1, format.Channels);
        int bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        bool silent = flags.HasFlag(AudioClientBufferFlags.Silent);
        for (int frame = 0; frame < frames; frame++)
        {
            float sample = 0;
            if (!silent)
            {
                int offset = frame * format.BlockAlign;
                sample = bytesPerSample switch
                {
                    4 when format.Encoding is WaveFormatEncoding.IeeeFloat or WaveFormatEncoding.Extensible
                        => Marshal.PtrToStructure<float>(buffer + offset),
                    3 => Read24Bit(buffer + offset) / 8388608f,
                    2 => Marshal.ReadInt16(buffer + offset) / 32768f,
                    _ => 0
                };
                if (channels > 1 && !float.IsFinite(sample)) sample = 0;
            }
            _fftBuffer[_fftPosition++] = sample;
            if (_fftPosition < _fftSize) continue;
            _fftPosition = 0;
            PublishSpectrum(processId, CalculateSpectrum());
        }
    }

    private void PublishSpectrum(int processId, IReadOnlyList<float> bands)
    {
        foreach (Action<int, IReadOnlyList<float>> handler in SpectrumUpdated?.GetInvocationList()
                     ?? Array.Empty<Delegate>())
        {
            try { handler(processId, bands); }
            catch { }
        }
    }

    private float[] CalculateSpectrum()
    {
        var values = new Complex[_fftSize];
        for (int index = 0; index < _fftSize; index++)
        {
            float window = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * index / (_fftSize - 1))));
            values[index].X = _fftBuffer[index] * window;
        }
        FastFourierTransform.FFT(true, (int)Math.Log2(_fftSize), values);
        var bands = new float[_bandCount];
        int half = _fftSize / 2;
        for (int band = 0; band < bands.Length; band++)
        {
            int low = Math.Max(1, (int)(Math.Pow((double)band / bands.Length, 2) * half));
            int high = Math.Min(half - 1, (int)(Math.Pow((double)(band + 1) / bands.Length, 2) * half));
            float peak = 0;
            for (int bin = low; bin <= Math.Max(low, high); bin++)
                peak = Math.Max(peak, MathF.Sqrt(values[bin].X * values[bin].X + values[bin].Y * values[bin].Y));
            float decibels = 20 * MathF.Log10(Math.Max(peak, 1e-10f));
            bands[band] = Math.Clamp((decibels + 60) / 60, 0, 1);
        }
        return bands;
    }

    private static int Read24Bit(IntPtr pointer)
    {
        int value = Marshal.ReadByte(pointer)
                    | Marshal.ReadByte(pointer, 1) << 8
                    | Marshal.ReadByte(pointer, 2) << 16;
        return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }

    private static async Task<NAudio.CoreAudioApi.Interfaces.IAudioClient> ActivateAsync(uint processId)
    {
        var parameters = new AudioClientActivationParameters
        {
            ActivationType = AudioClientActivationType.ProcessLoopback,
            ProcessLoopback = new AudioClientProcessLoopbackParameters
            {
                TargetProcessId = processId,
                Mode = ProcessLoopbackMode.IncludeTargetProcessTree
            }
        };
        IntPtr parameterMemory = Marshal.AllocCoTaskMem(Marshal.SizeOf<AudioClientActivationParameters>());
        IntPtr variantMemory = Marshal.AllocCoTaskMem(Marshal.SizeOf<PropVariant>());
        try
        {
            Marshal.StructureToPtr(parameters, parameterMemory, false);
            var variant = new PropVariant
            {
                VariantType = VtBlob,
                Blob = new Blob
                {
                    Size = Marshal.SizeOf<AudioClientActivationParameters>(),
                    Data = parameterMemory
                }
            };
            Marshal.StructureToPtr(variant, variantMemory, false);
            var completion = new ActivationCompletion();
            Guid audioClientId = typeof(NAudio.CoreAudioApi.Interfaces.IAudioClient).GUID;
            int result = ActivateAudioInterfaceAsync(
                ProcessLoopbackDevice,
                ref audioClientId,
                variantMemory,
                completion,
                out _);
            Marshal.ThrowExceptionForHR(result);
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            Marshal.FreeCoTaskMem(variantMemory);
            Marshal.FreeCoTaskMem(parameterMemory);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopAsync().GetAwaiter().GetResult();
        _disposed = true;
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ActivationCompletion : IActivateAudioInterfaceCompletionHandler
    {
        private readonly TaskCompletionSource<NAudio.CoreAudioApi.Interfaces.IAudioClient> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<NAudio.CoreAudioApi.Interfaces.IAudioClient> Task => _completion.Task;

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            try
            {
                activateOperation.GetActivateResult(out int result, out object activated);
                Marshal.ThrowExceptionForHR(result);
                _completion.TrySetResult((NAudio.CoreAudioApi.Interfaces.IAudioClient)activated);
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }
    }

    private enum AudioClientActivationType { Default, ProcessLoopback }
    private enum ProcessLoopbackMode { IncludeTargetProcessTree, ExcludeTargetProcessTree }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientProcessLoopbackParameters
    {
        public uint TargetProcessId;
        public ProcessLoopbackMode Mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParameters
    {
        public AudioClientActivationType ActivationType;
        public AudioClientProcessLoopbackParameters ProcessLoopback;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Size; public IntPtr Data; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort VariantType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public Blob Blob;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid interfaceId,
        IntPtr activationParameters,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);
}
