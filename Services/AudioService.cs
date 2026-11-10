using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NAudio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BonbonPet.Services;

/// <summary>
/// Reads the default speaker's WASAPI loopback stream. Samples are measured in
/// memory and discarded; this service never opens a microphone or writes audio.
/// </summary>
public sealed class AudioService : IDisposable
{
    private static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly short[] OriginalSqueak = GenerateOriginalSqueak();
    private static readonly Lazy<DuckSounds> DuckSqueak = new(LoadDuckSounds, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly object _captureGate = new();
    private readonly object _squeakGate = new();
    private readonly Timer _monitor;
    private WasapiLoopbackCapture? _capture;
    private MMDevice? _device;
    private string? _deviceId;
    private volatile WaveOutEvent? _squeakPlayer;
    private string? _outputDeviceId;
    private readonly SqueakProvider _squeakProvider = new();
    private short[] _squeakSamples = OriginalSqueak;
    private short[] _pressSamples = OriginalSqueak;
    private short[] _releaseSamples = Array.Empty<short>();
    private bool _pressArmed;
    private volatile bool _soundActive = true;
    private volatile bool _outputAllowed;
    private volatile bool _outputReconnect;
    private int _outputPending, _outputWorkerRunning;
    private long _nextOutputCheck;
    private string _squeakKind = "original";
    private string? _squeakPath;
    private volatile bool _enabled;
    private volatile bool _disposed;
    private volatile bool _running;
    private volatile float _rawLevel;
    private volatile float _level;
    private volatile float _squeakVolume = .45f;
    private volatile string _status = "音频律动已关闭";
    private long _lastAudioTick;
    private long _ignoreUntilTick;
    private long _nextDeviceCheck;
    private int _inMonitor;

    public float Level => _enabled ? _level : 0;
    public string Status => _status;
    public bool IsRunning => _running;
    public bool IsSoundReady => !_disposed && _soundActive && _squeakPlayer is not null;
    public static bool HasDuckAssets => File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "Ya1.mp3"))
        && File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "Ya2.mp3"));
    public event Action? StatusChanged;

    public float SqueakVolume
    {
        get => _squeakVolume;
        set => _squeakVolume = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .45f;
    }

    public AudioService() : this(true) { }

    // Offline sound tests can exercise the real stream without opening a speaker.
    internal AudioService(bool prepareOutput)
    {
        // A 25 Hz meter is sufficient for a desktop animation. The audio thread
        // only calculates a meter and does no UI work.
        _monitor = new Timer(Monitor, null, 40, 40);
        _outputAllowed = prepareOutput;
        _nextOutputCheck = Environment.TickCount64 + 3000;
        if (prepareOutput) RequestOutputUpdate();
    }

    public void SetSoundActive(bool active)
    {
        if (_disposed) return;
        _outputAllowed = true;
        if (_soundActive == active && (!active || _squeakPlayer is not null)) return;
        _soundActive = active;
        if (!active)
        {
            lock (_squeakGate) _pressArmed = false;
            _squeakProvider.Clear();
        }
        RequestOutputUpdate();
    }

    public void Start()
    {
        lock (_captureGate)
        {
            if (_disposed) return;
            _enabled = true;
            if (_capture is not null) return;
            OpenDefaultDevice();
            _nextDeviceCheck = Environment.TickCount64 + 3000;
        }
    }

    public void Stop()
    {
        lock (_captureGate)
        {
            _enabled = false;
            CloseCapture();
            _rawLevel = 0;
            _level = 0;
            SetStatus("音频律动已关闭");
        }
    }

    /// <summary>Changes the cached click sound. Custom files are decoded once, never while clicking.</summary>
    public void ConfigureSqueak(string kind, string? path)
    {
        kind = (kind ?? "original").Trim().ToLowerInvariant();
        if (kind is not ("original" or "duck" or "custom"))
            throw new InvalidDataException("请选择原版、橡皮鸭或自定义音效。");
        string? fullPath = null;
        if (kind == "custom")
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("请先选择一个 WAV 或 MP3 音效文件。");
            try { fullPath = Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { throw new InvalidDataException("音效文件路径无法读取。", ex); }
        }
        lock (_squeakGate)
        {
            if (_disposed || (kind == _squeakKind && string.Equals(fullPath, _squeakPath, StringComparison.OrdinalIgnoreCase))) return;
        }

        // Validation and decoding occur before replacing the current cache, so
        // a failed import leaves the previously selected click sound intact.
        short[] next;
        short[] press;
        short[] release = Array.Empty<short>();
        if (kind == "duck")
        {
            var duck = DuckSqueak.Value;
            next = duck.Full; press = duck.Press; release = duck.Release;
        }
        else { next = kind == "original" ? OriginalSqueak : DecodeCustomSqueak(fullPath!); press = next; }
        lock (_squeakGate)
        {
            if (_disposed) return;
            _squeakSamples = next;
            _pressSamples = press;
            _releaseSamples = release;
            _pressArmed = false;
            _squeakKind = kind;
            _squeakPath = fullPath;
        }
        _squeakProvider.Clear();
        RequestOutputUpdate();
    }

    public void PressSqueak()
    {
        float volume = _squeakVolume;
        if (_disposed || !_soundActive || volume <= .001f) return;
        short[] samples;
        lock (_squeakGate) { samples = _pressSamples; _pressArmed = _releaseSamples.Length > 0; }
        EnqueueSound(samples, volume);
    }

    public void ReleaseSqueak()
    {
        short[] samples;
        lock (_squeakGate)
        {
            if (!_pressArmed) return;
            _pressArmed = false; samples = _releaseSamples;
        }
        float volume = _squeakVolume;
        if (_disposed || !_soundActive || volume <= .001f) return;
        EnqueueSound(samples, volume, releasePart: true);
    }

    public void PlaySqueak()
    {
        float volume = _squeakVolume;
        if (_disposed || !_soundActive || volume <= .001f) return;
        short[] samples;
        lock (_squeakGate) { samples = _squeakSamples; _pressArmed = false; }
        EnqueueSound(samples, volume);
    }

    private void EnqueueSound(short[] samples, float volume, bool releasePart = false)
    {
        if (samples.Length == 0) return;
        int pendingSamples = _squeakProvider.Enqueue(samples, volume, releasePart);
        Interlocked.Exchange(ref _ignoreUntilTick,
            Environment.TickCount64 + (long)Math.Ceiling(pendingSamples * 1000.0 / 44100) + 195);
        if (_squeakPlayer is null) RequestOutputUpdate();
    }

    private void RequestOutputUpdate()
    {
        if (!_outputAllowed) return;
        Interlocked.Exchange(ref _outputPending, 1);
        if (Interlocked.CompareExchange(ref _outputWorkerRunning, 1, 0) == 0)
            ThreadPool.QueueUserWorkItem(UpdateOutputWorker);
    }

    private void UpdateOutputWorker(object? state)
    {
        try
        {
            do
            {
                Interlocked.Exchange(ref _outputPending, 0);
                if (_disposed || !_soundActive) { CloseSqueakPlayer(); continue; }
                string? currentDevice = null;
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    currentDevice = device.ID;
                    if (_outputDeviceId != currentDevice) _outputReconnect = true;
                }
                catch { }
                if (_outputReconnect || _squeakPlayer is null || _squeakPlayer.PlaybackState != PlaybackState.Playing)
                {
                    _outputReconnect = false;
                    CloseSqueakPlayer();
                    try
                    {
                        // Three buffers give the driver room to survive a short
                        // scheduling pause while the desktop is being dragged.
                        // The former two 20 ms buffers could underrun even though
                        // the clip was already decoded and the handle was warm.
                        var player = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
                        try { player.Init(_squeakProvider); player.Play(); }
                        catch { player.Dispose(); throw; }
                        _squeakPlayer = player;
                        _outputDeviceId = currentDevice;
                        // Init/Play can take a driver hundreds of milliseconds;
                        // recheck visibility after it completes on this worker.
                        if (_disposed || !_soundActive) CloseSqueakPlayer();
                    }
                    catch { _nextOutputCheck = Environment.TickCount64 + 3000; }
                }
            } while (Interlocked.CompareExchange(ref _outputPending, 0, 0) != 0);
        }
        finally
        {
            Volatile.Write(ref _outputWorkerRunning, 0);
            if (Interlocked.CompareExchange(ref _outputPending, 0, 0) != 0) RequestOutputUpdate();
        }
    }

    private void MonitorOutput(long now)
    {
        if (!_outputAllowed || !_soundActive || now < Interlocked.Read(ref _nextOutputCheck)) return;
        Interlocked.Exchange(ref _nextOutputCheck, now + 3000);
        RequestOutputUpdate();
    }

    private void RestartSoundOutput()
    {
        _outputReconnect = true;
        RequestOutputUpdate();
    }

    private void CloseSqueakPlayer()
    {
        // Only the output worker touches the driver. UI click paths never
        // wait behind waveOutReset/Stop/Dispose or a device initialization.
        var player = _squeakPlayer;
        _squeakPlayer = null;
        _outputDeviceId = null;
        try { player?.Stop(); } catch { }
        try { player?.Dispose(); } catch { }
    }

    private void Monitor(object? state)
    {
        if (_disposed || Interlocked.Exchange(ref _inMonitor, 1) != 0) return;
        try
        {
            var now = Environment.TickCount64;
            MonitorOutput(now);
            var ignoreUntil = Interlocked.Read(ref _ignoreUntilTick);
            var target = _enabled && (now < ignoreUntil || now - Interlocked.Read(ref _lastAudioTick) < 240)
                ? _rawLevel : 0;
            // Fast attack and a soft release avoid jitter between short blocks.
            _level += (target - _level) * (target > _level ? .55f : .15f);
            if (_level < .001f) _level = 0;

            if (!_enabled || now < Interlocked.Read(ref _nextDeviceCheck)) return;
            lock (_captureGate)
            {
                if (!_enabled || _disposed) return;
                _nextDeviceCheck = now + 3000;
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    using var current = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (_capture is null || !_running || current.ID != _deviceId)
                    {
                        CloseCapture();
                        OpenDefaultDevice();
                        RestartSoundOutput();
                    }
                }
                catch
                {
                    CloseCapture();
                    SetStatus("没有可用的播放设备，稍后自动重试");
                }
            }
        }
        catch
        {
            // The timer must not terminate the application on a driver change.
        }
        finally
        {
            Volatile.Write(ref _inMonitor, 0);
        }
    }

    // Caller holds _captureGate. Device checks occur only once every 3 seconds.
    private void OpenDefaultDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _deviceId = _device.ID;
            var capture = new WasapiLoopbackCapture(_device);
            _capture = capture;
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();
            _running = true;
            SetStatus("正在分析系统播放音频");
        }
        catch
        {
            CloseCapture();
            SetStatus("暂时无法读取系统声音，稍后自动重试");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (_disposed || !_enabled || sender is not WasapiLoopbackCapture capture || !ReferenceEquals(capture, _capture)) return;
        var now = Environment.TickCount64;
        if (now < Interlocked.Read(ref _ignoreUntilTick)) return;
        _rawLevel = MeasureLevel(args.Buffer, args.BytesRecorded, capture.WaveFormat);
        Interlocked.Exchange(ref _lastAudioTick, now);
    }

    // Kept separate from device I/O so supported driver sample formats can be
    // checked deterministically without changing the user's speaker volume.
    internal static float MeasureLevel(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        var bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample < 1 || bytesRecorded < bytesPerSample) return 0;
        var floatSamples = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format is WaveFormatExtensible extensible && extensible.SubFormat == FloatSubtype;
        double sum = 0;
        double peak = 0;
        var count = 0;

        // Include all channels, including audio panned entirely to one speaker.
        for (var offset = 0; offset + bytesPerSample <= bytesRecorded; offset += bytesPerSample)
        {
            double sample;
            if (floatSamples && bytesPerSample == 4)
                sample = BitConverter.ToSingle(buffer, offset);
            else if (floatSamples && bytesPerSample == 8)
                sample = BitConverter.ToDouble(buffer, offset);
            else if (bytesPerSample == 2)
                sample = (short)(buffer[offset] | buffer[offset + 1] << 8) / 32768.0;
            else if (bytesPerSample == 3)
            {
                var value = buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;
                if ((value & 0x800000) != 0) value |= unchecked((int)0xff000000);
                sample = value / 8388608.0;
            }
            else if (bytesPerSample == 4)
                sample = BitConverter.ToInt32(buffer, offset) / 2147483648.0;
            else if (bytesPerSample == 1)
                sample = (buffer[offset] - 128) / 128.0;
            else
                continue;
            if (!double.IsFinite(sample)) continue;
            sum += sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
            count++;
        }
        if (count == 0) return 0;
        var amplitude = Math.Sqrt(sum / count) * .8 + peak * .2;
        // A decibel meter remains expressive at normal desktop listening volume.
        // The noise gate prevents dithering/noise from moving a silent pet.
        var level = amplitude < .0015 ? 0 : Math.Clamp((20 * Math.Log10(amplitude) + 54) / 48, 0, 1);
        return (float)level;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        if (_disposed || !ReferenceEquals(sender, _capture)) return;
        _running = false;
        _rawLevel = 0;
        if (_enabled)
        {
            SetStatus("播放设备已变化，正在重新连接");
            Interlocked.Exchange(ref _nextDeviceCheck, Environment.TickCount64 + 1000);
        }
        // Do not dispose the capture from its own audio thread.
    }

    private void CloseCapture()
    {
        var capture = _capture;
        _capture = null;
        _running = false;
        _deviceId = null;
        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            try { capture.StopRecording(); } catch { }
            try { capture.Dispose(); } catch { }
        }
        try { _device?.Dispose(); } catch { }
        _device = null;
    }

    private void SetStatus(string status)
    {
        if (_status == status) return;
        _status = status;
        // Subscribers may marshal this notification to their UI thread.
        try { StatusChanged?.Invoke(); } catch { }
    }

    public void Dispose()
    {
        lock (_captureGate)
        {
            if (_disposed) return;
            _disposed = true;
            _enabled = false;
            _monitor.Dispose();
            CloseCapture();
            _rawLevel = _level = 0;
            _squeakProvider.Clear();
            RequestOutputUpdate();
        }
    }

    private static short[] DecodeCustomSqueak(string path)
    {
        const int maxSamples = 44100 * 10;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new InvalidDataException("找不到这个音效文件。");
            if (info.Length > 20 * 1024 * 1024) throw new InvalidDataException("音效文件超过 20 MB，请选择较小的 WAV 或 MP3。");
            string extension = info.Extension.ToLowerInvariant();
            if (extension is not (".wav" or ".mp3")) throw new InvalidDataException("自定义点击音效支持 WAV 和 MP3。");
            using var reader = new AudioFileReader(path);
            double duration = reader.TotalTime.TotalSeconds;
            if (!double.IsFinite(duration) || duration <= 0) throw new InvalidDataException("这个音效没有可播放的音频。");
            if (duration > 10.00001) throw new InvalidDataException("音效需要在 10 秒以内，请先截取较短片段。");
            if (reader.WaveFormat.Channels is < 1 or > 32 || reader.WaveFormat.SampleRate <= 0)
                throw new InvalidDataException("这个音效的声道或采样率无法读取。");
            ISampleProvider mono = reader.WaveFormat.Channels == 1 ? reader : new MonoMixProvider(reader);
            ISampleProvider source = mono.WaveFormat.SampleRate == 44100 ? mono : new WdlResamplingSampleProvider(mono, 44100);
            var result = new List<short>(Math.Min(maxSamples, (int)Math.Ceiling(duration * 44100)));
            var block = new float[4096];
            int read;
            while ((read = source.Read(block, 0, block.Length)) > 0)
            {
                if (result.Count + read > maxSamples) throw new InvalidDataException("音效需要在 10 秒以内，请先截取较短片段。");
                for (int i = 0; i < read; i++)
                    result.Add(float.IsFinite(block[i]) ? (short)(Math.Clamp(block[i], -1, 1) * short.MaxValue) : (short)0);
            }
            if (result.Count == 0) throw new InvalidDataException("这个音效没有可播放的音频。");
            return result.ToArray();
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or FormatException or System.Runtime.InteropServices.COMException or MmException)
        { throw new InvalidDataException("无法解码音效，请选择可正常播放的 WAV 或 MP3。", ex); }
    }

    private static short[] GenerateOriginalSqueak()
    {
        // Keep the original v0.1 waveform exactly; only its cache location changes.
        const int rate = 44100;
        const double duration = .225;
        var samples = new short[(int)(rate * duration)];
        var random = new Random(73);
        double phase = 0;
        double filteredNoise = 0;
        for (var index = 0; index < samples.Length; index++)
        {
            var time = (double)index / rate;
            var fraction = time / duration;
            var pitch = 760 + 550 * Math.Sin(Math.PI * fraction) - 250 * fraction;
            pitch *= 1 + .018 * Math.Sin(2 * Math.PI * 34 * time);
            phase += 2 * Math.PI * pitch / rate;
            var onset = Math.Min(1, time / .008);
            var envelope = onset * Math.Pow(Math.Max(0, 1 - fraction), .75);
            envelope *= .72 + .28 * Math.Sin(Math.PI * fraction);
            if (fraction > .58 && fraction < .66) envelope *= .3;
            filteredNoise += ((random.NextDouble() * 2 - 1) - filteredNoise) * .2;
            var tone = Math.Sin(phase) * .62 + Math.Sin(phase * 2) * .26 + Math.Sin(phase * 3) * .12;
            var value = (tone + filteredNoise * .13) * envelope * .67;
            samples[index] = (short)(Math.Clamp(value, -1, 1) * short.MaxValue);
        }
        return samples;
    }

    private static DuckSounds LoadDuckSounds()
    {
        if (!HasDuckAssets) return new DuckSounds(OriginalSqueak, Array.Empty<short>(), OriginalSqueak);
        string folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");
        short[] press = DecodeCustomSqueak(Path.Combine(folder, "Ya1.mp3"));
        short[] release = DecodeCustomSqueak(Path.Combine(folder, "Ya2.mp3"));
        var full = new short[press.Length + release.Length];
        Array.Copy(press, full, press.Length);
        Array.Copy(release, 0, full, press.Length, release.Length);
        return new DuckSounds(press, release, full);
    }

    private sealed record DuckSounds(short[] Press, short[] Release, short[] Full);

    private sealed class MonoMixProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _buffer = Array.Empty<float>();
        public WaveFormat WaveFormat { get; }
        public MonoMixProvider(ISampleProvider source)
        {
            _source = source; _channels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }
        public int Read(float[] buffer, int offset, int count)
        {
            int needed = checked(count * _channels);
            if (_buffer.Length < needed) _buffer = new float[needed];
            int frames = _source.Read(_buffer, 0, needed) / _channels;
            for (int frame = 0; frame < frames; frame++)
            {
                double sum = 0;
                for (int channel = 0; channel < _channels; channel++)
                {
                    float sample = _buffer[frame * _channels + channel];
                    if (float.IsFinite(sample)) sum += sample;
                }
                buffer[offset + frame] = (float)(sum / _channels);
            }
            return frames;
        }
    }

    internal sealed class SqueakProvider : IWaveProvider
    {
        // One latest gesture, with at most one press and its matching release.
        private const int Capacity = 2;
        private readonly object _gate = new();
        private readonly Segment[] _segments = new Segment[Capacity];
        private int _head, _count, _pendingSamples;
        private bool _releaseQueued;
        public WaveFormat WaveFormat { get; } = new(44100, 16, 1);

        public int Enqueue(short[] samples, float volume, bool releasePart = false)
        {
            lock (_gate)
            {
                if (samples.Length == 0) return _pendingSamples;
                // A fresh pinch or preview replaces the previous sound, even
                // mid-clip. Its release appends to that latest press only; rapid
                // clicks never build a backlog or call Stop/Init on the driver.
                if (!releasePart) ClearCore();
                else if (_releaseQueued) return _pendingSamples;
                if (releasePart) _releaseQueued = true;
                _segments[(_head + _count) % Capacity] = new Segment(samples, volume);
                _count++; _pendingSamples += samples.Length;
                return _pendingSamples;
            }
        }

        public void Clear() { lock (_gate) ClearCore(); }
        private void ClearCore()
        {
            Array.Clear(_segments); _head = _count = _pendingSamples = 0;
            _releaseQueued = false;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            lock (_gate)
            {
                int remaining = count / 2;
                while (_count > 0 && remaining > 0)
                {
                    ref var segment = ref _segments[_head];
                    int read = Math.Min(remaining, segment.Samples.Length - segment.Position);
                    for (int i = 0; i < read; i++)
                    {
                        short sample = (short)(segment.Samples[segment.Position++] * segment.Volume);
                        buffer[offset++] = (byte)sample; buffer[offset++] = (byte)(sample >> 8);
                    }
                    remaining -= read; _pendingSamples -= read;
                    if (segment.Position == segment.Samples.Length)
                    {
                        segment = default; _head = (_head + 1) % Capacity; _count--;
                    }
                }
                // Never return zero between gestures: one warmed output handle
                // continues to run quietly, with no driver calls on mouse input.
                return count;
            }
        }

        private struct Segment
        {
            public short[] Samples;
            public float Volume;
            public int Position;
            public Segment(short[] samples, float volume)
            { Samples = samples; Volume = volume; Position = 0; }
        }
    }
}
