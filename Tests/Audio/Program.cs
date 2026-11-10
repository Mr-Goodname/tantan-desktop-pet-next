using System.Diagnostics;
using System.Text.Json;
using BonbonPet.Services;
using NAudio.Wave;

// An integration check, using the actual default speaker and actual WASAPI
// loopback capture. The short test tone is deliberately quiet and right-only.
var outputPath = args.Length > 0 ? args[0] : Path.GetFullPath("../audio-result.json");
var formatChecks = CheckDriverFormats();
using var audio = new AudioService();
audio.Start();
Thread.Sleep(600);
var running = audio.IsRunning;
var initialStatus = audio.Status;
var baselinePeak = Measure(audio, 400);
float tonePeak = 0;
string? playbackError = null;
try
{
    using var output = new WaveOutEvent { DesiredLatency = 60, NumberOfBuffers = 2 };
    output.Init(new RightOnlyTone());
    output.Play();
    tonePeak = Measure(audio, 1200);
    output.Stop();
}
catch (Exception error) { playbackError = error.Message; }
Measure(audio, 1200);
var beforeSqueakPeak = Measure(audio, 240);
audio.PlaySqueak();
Thread.Sleep(80);
var holdSamples = new List<float>();
var holdTimer = Stopwatch.StartNew();
while (holdTimer.ElapsedMilliseconds < 180)
{
    holdSamples.Add(audio.Level);
    Thread.Sleep(20);
}
var squeakPeak = holdSamples.Max();
var squeakHoldsMusicMeter = Monotonic(holdSamples);
Measure(audio, 300);
audio.Stop();
var stopsAndClearsMeter = !audio.IsRunning && audio.Level == 0;
audio.Start();
Thread.Sleep(300);
var restarts = audio.IsRunning;
audio.Stop();
var sharedCaptureChecks = CheckSharedCapture();

var result = new
{
    Test = "Actual default-speaker playback and WASAPI loopback",
    TimestampUtc = DateTimeOffset.UtcNow,
    Running = running,
    InitialStatus = initialStatus,
    DriverFormatChecks = formatChecks,
    BaselinePeak = baselinePeak,
    RightOnlyTonePeak = tonePeak,
    PlaybackDetected = running && playbackError is null && tonePeak > .08f,
    ToneIsolatedFromOtherAudio = baselinePeak < .03f,
    BeforeSqueakPeak = beforeSqueakPeak,
    SqueakPeak = squeakPeak,
    SqueakHoldSamples = holdSamples,
    SqueakFeedbackSuppressed = squeakHoldsMusicMeter,
    FeedbackCheck = "During the squeak, the public meter converges monotonically to its held music value; ongoing system audio is not muted.",
    StopsAndClearsMeter = stopsAndClearsMeter,
    Restarts = restarts,
    SharedCaptureChecks = sharedCaptureChecks,
    PlaybackError = playbackError
};
var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
File.WriteAllText(outputPath, json);
Console.WriteLine(json);
Environment.ExitCode = result.PlaybackDetected && result.SqueakFeedbackSuppressed && formatChecks.AllPassed && stopsAndClearsMeter && restarts && sharedCaptureChecks ? 0 : 1;

static bool CheckSharedCapture()
{
    using var meter = new AudioService(false);
    var first = new AudioService(meter);
    using var second = new AudioService(meter);
    first.Start(); second.Start(); meter.Start();
    Thread.Sleep(250);
    bool sharedStarted = meter.IsRunning && first.IsRunning && second.IsRunning;
    first.Stop(); first.Dispose();
    bool survivesRemoval = meter.IsRunning && second.IsRunning;
    meter.Stop();
    return sharedStarted && survivesRemoval && !second.IsRunning && second.Level == 0;
}

static float Measure(AudioService audio, int milliseconds)
{
    var timer = Stopwatch.StartNew();
    float peak = 0;
    while (timer.ElapsedMilliseconds < milliseconds)
    {
        peak = Math.Max(peak, audio.Level);
        Thread.Sleep(20);
    }
    return peak;
}

static bool Monotonic(List<float> samples)
{
    var increases = 0;
    var decreases = 0;
    for (var index = 1; index < samples.Count; index++)
    {
        var difference = samples[index] - samples[index - 1];
        if (difference > .00002f) increases++;
        if (difference < -.00002f) decreases++;
    }
    return samples.Count >= 4 && (increases == 0 || decreases == 0);
}

static DriverChecks CheckDriverFormats()
{
    const int frames = 1024;
    var stereoFloat = new byte[frames * 2 * 4];
    var stereoPcm = new byte[frames * 2 * 2];
    // Only the right speaker contains signal; metering every other individual
    // sample would miss it completely.
    for (var frame = 0; frame < frames; frame++)
    {
        var sample = (float)(Math.Sin(2 * Math.PI * frame / 32) * .08);
        BitConverter.TryWriteBytes(stereoFloat.AsSpan(frame * 8 + 4, 4), sample);
        BitConverter.TryWriteBytes(stereoPcm.AsSpan(frame * 4 + 2, 2), (short)(sample * 32767));
    }
    var floatFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    var floatRight = AudioService.MeasureLevel(stereoFloat, stereoFloat.Length, floatFormat);
    var pcmRight = AudioService.MeasureLevel(stereoPcm, stereoPcm.Length, new WaveFormat(48000, 16, 2));
    var silence = AudioService.MeasureLevel(new byte[stereoFloat.Length], stereoFloat.Length, floatFormat);
    var nonFinite = new byte[32];
    for (var offset = 0; offset < nonFinite.Length; offset += 4)
        BitConverter.TryWriteBytes(nonFinite.AsSpan(offset, 4), float.NaN);
    var invalid = AudioService.MeasureLevel(nonFinite, nonFinite.Length, floatFormat);
    return new DriverChecks(floatRight, pcmRight, silence, invalid,
        floatRight > .1 && pcmRight > .1 && Math.Abs(floatRight - pcmRight) < .002 && silence == 0 && invalid == 0);
}

record DriverChecks(float RightOnlyFloatLevel, float RightOnlyPcm16Level, float SilenceLevel, float NonFiniteLevel, bool AllPassed);

sealed class RightOnlyTone : ISampleProvider
{
    private const int Rate = 48000;
    private const int Frames = Rate * 9 / 10;
    private int _frame;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);

    public int Read(float[] buffer, int offset, int count)
    {
        var frames = Math.Min(count / 2, Frames - _frame);
        for (var index = 0; index < frames; index++)
        {
            var envelope = Math.Min(1, _frame / 480.0) * Math.Min(1, (Frames - _frame) / 480.0);
            buffer[offset++] = 0;
            buffer[offset++] = (float)(Math.Sin(2 * Math.PI * 330 * _frame / Rate) * .035 * envelope);
            _frame++;
        }
        return frames * 2;
    }
}
