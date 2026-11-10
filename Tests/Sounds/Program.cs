using System.Reflection;
using System.Security.Cryptography;
using BonbonPet.Services;
using NAudio.Wave;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); }
var privateSamples = typeof(AudioService).GetField("_squeakSamples", BindingFlags.Instance | BindingFlags.NonPublic)!;
short[] Samples(AudioService service) => (short[])privateSamples.GetValue(service)!;
byte[] Bytes(short[] samples) { var bytes = new byte[samples.Length * 2]; Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length); return bytes; }
using var service = new AudioService(false);
var original = Samples(service);
Check(original.Length == 9922, "Original 225ms sample length retained");
// Golden hash verified against the original published v0.1 assembly before changing its audio service.
Check(Convert.ToHexString(SHA256.HashData(Bytes(original))) == "6C776F61705087F03B1FA12D7ADDDD648D69CE80DF9746432B242A5CB16F5FC6", "Original waveform retains verified v0.1 sample hash");
service.ConfigureSqueak("duck", null);
var duck = Samples(service);
var privatePress = typeof(AudioService).GetField("_pressSamples", BindingFlags.Instance | BindingFlags.NonPublic)!;
var privateRelease = typeof(AudioService).GetField("_releaseSamples", BindingFlags.Instance | BindingFlags.NonPublic)!;
var press = (short[])privatePress.GetValue(service)!;
var release = (short[])privateRelease.GetValue(service)!;
Check(AudioService.HasDuckAssets ? press.Length > 1000 && release.Length > 1000 && duck.SequenceEqual(press.Concat(release))
    : ReferenceEquals(original, duck) && ReferenceEquals(press, original) && release.Length == 0, "Duck uses supplied pair when present, synthesized fallback otherwise");
var providerField = typeof(AudioService).GetField("_squeakProvider", BindingFlags.Instance | BindingFlags.NonPublic)!;
var stream = (AudioService.SqueakProvider)providerField.GetValue(service)!;
service.SqueakVolume = 1;
short[] ReadSamples(int count)
{
    var bytes = new byte[count * 2];
    Check(stream.Read(bytes, 0, bytes.Length) == bytes.Length, "Warmed stream returns full buffer even while silent");
    var result = new short[count]; Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length); return result;
}
if (AudioService.HasDuckAssets)
{
service.PressSqueak(); service.ReleaseSqueak();
Check(ReadSamples(duck.Length).SequenceEqual(duck), "Quick click finishes entire Ya1 then Ya2 without truncation or overlap");
Check(ReadSamples(256).All(x => x == 0), "Idle output stays silent after full duck sound");
service.PressSqueak();
Check(ReadSamples(press.Length).SequenceEqual(press), "Mouse press plays Ya1 exactly once");
Check(ReadSamples(4410).All(x => x == 0), "Holding after Ya1 stays silent without looping");
service.ReleaseSqueak();
Check(ReadSamples(release.Length).SequenceEqual(release), "Mouse release plays Ya2 after a long hold");
service.ReleaseSqueak(); Check(ReadSamples(256).All(x => x == 0), "Duplicate release cannot play a second Ya2");
service.PlaySqueak(); Check(ReadSamples(duck.Length).SequenceEqual(duck), "Preview button plays the exact complete Ya1 and Ya2 pair");
service.PressSqueak(); service.ReleaseSqueak(); ReadSamples(128);
service.PressSqueak(); service.ReleaseSqueak();
Check(ReadSamples(duck.Length).SequenceEqual(duck), "Next click interrupts an in-progress pair and starts the latest Ya1 then Ya2");
for (int i = 0; i < 100; i++) { service.PressSqueak(); service.ReleaseSqueak(); }
var burst = ReadSamples(duck.Length + 256);
Check(burst.Take(duck.Length).SequenceEqual(duck) && burst.Skip(duck.Length).All(x => x == 0), "One hundred rapid clicks produce exactly the latest pair then silence");
service.PressSqueak(); ReadSamples(128); service.PlaySqueak(); service.ReleaseSqueak();
Check(ReadSamples(duck.Length + 256).SequenceEqual(duck.Concat(new short[256])), "Preview replaces the current press and disarms its old release");
}
else
{
    service.PressSqueak(); service.ReleaseSqueak();
    Check(ReadSamples(original.Length).SequenceEqual(original), "Public distribution press uses complete synthesized sound");
    Check(ReadSamples(256).All(x => x == 0), "Public fallback release adds no missing clip or duplicate sound");
}
stream.Clear();
// Distinct PCM makes any leaked tail from a previous gesture observable.
var previousPress = Enumerable.Range(0, 1024).Select(i => (short)(i + 500)).ToArray();
var previousRelease = Enumerable.Repeat((short)-1200, 512).ToArray();
var latestPress = Enumerable.Repeat((short)2400, 256).ToArray();
var latestRelease = Enumerable.Repeat((short)-3600, 128).ToArray();
stream.Enqueue(previousPress, 1); stream.Enqueue(previousRelease, 1, releasePart: true);
Check(ReadSamples(128).SequenceEqual(previousPress.Take(128)), "First driver refill contains the beginning of the active sound");
stream.Enqueue(latestPress, 1); stream.Enqueue(latestRelease, 1, releasePart: true);
var latestPair = latestPress.Concat(latestRelease).ToArray();
Check(ReadSamples(latestPair.Length + 256).SequenceEqual(latestPair.Concat(new short[256])), "A new gesture cuts old press and queued release without leaving a backlog");
stream.Clear();
stream.Enqueue(previousPress, 1); stream.Enqueue(previousRelease, 1, releasePart: true);
ReadSamples(previousPress.Length + 128);
stream.Enqueue(latestPress, 1); stream.Enqueue(latestRelease, 1, releasePart: true);
Check(ReadSamples(latestPair.Length + 256).SequenceEqual(latestPair.Concat(new short[256])), "A new gesture also interrupts an already playing release");
stream.Clear();
stream.Enqueue(latestPress, 1);
ReadSamples(latestPress.Length);
stream.Enqueue(latestRelease, 1, releasePart: true);
Check(ReadSamples(latestRelease.Length).SequenceEqual(latestRelease), "Release still plays once if its press already finished during a hold");
stream.Enqueue(latestRelease, 1, releasePart: true);
Check(ReadSamples(256).All(x => x == 0), "Duplicate release cannot start an extra clip in the provider");
stream.Clear();
var allocationBuffer = new byte[256];
for (int i = 0; i < 20; i++) { stream.Enqueue(press, .5f); stream.Read(allocationBuffer, 0, allocationBuffer.Length); stream.Clear(); }
long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 1000; i++) { stream.Enqueue(press, .5f); stream.Read(allocationBuffer, 0, allocationBuffer.Length); stream.Clear(); }
Check(GC.GetAllocatedBytesForCurrentThread() == allocatedBefore, "Clip enqueue/read/reset performs no hot-path allocation");
service.ConfigureSqueak("duck", "ignored path"); Check(ReferenceEquals(duck, Samples(service)), "Preset cache unchanged by irrelevant custom path");
service.ConfigureSqueak("original", null); Check(ReferenceEquals(original, Samples(service)), "Original preset reuses static cache");
service.PressSqueak(); ReadSamples(128); service.PressSqueak(); service.ReleaseSqueak();
Check(ReadSamples(original.Length + 256).SequenceEqual(original.Concat(new short[256])), "Original sound restarts on the newest press with no queued tail");

string folder = Path.Combine(AppContext.BaseDirectory, "generated"); Directory.CreateDirectory(folder);
string silent = Path.Combine(folder, "silent.wav"), stereo = Path.Combine(folder, "stereo.wav");
using (var writer = new WaveFileWriter(silent, new WaveFormat(22050, 16, 1))) writer.Write(new byte[22050 / 5 * 2], 0, 22050 / 5 * 2);
service.ConfigureSqueak("custom", silent); var custom = Samples(service);
Check(custom.Length is >= 8800 and <= 8840 && custom.All(x => x == 0), "Silent 22050Hz WAV decoded and resampled to mono44100");
File.Delete(silent); service.ConfigureSqueak("custom", silent);
Check(ReferenceEquals(custom, Samples(service)), "Same custom path no-ops without reopening removed file");
using (var writer = new WaveFileWriter(stereo, new WaveFormat(22050, 16, 2)))
    for (int i = 0; i < 4410; i++) { writer.WriteSample(.2f); writer.WriteSample(0); }
service.ConfigureSqueak("custom", stereo); custom = Samples(service);
Check(custom.Length is >= 8800 and <= 8840 && custom.Skip(100).Take(8000).Average(x => (double)x) is > 3200 and < 3350, "Stereo downmix averages both channels then resamples");
string tenSeconds = Path.Combine(folder, "ten.wav");
using (var writer = new WaveFileWriter(tenSeconds, new WaveFormat(22050, 16, 1))) writer.Write(new byte[22050 * 10 * 2], 0, 22050 * 10 * 2);
service.ConfigureSqueak("custom", tenSeconds); Check(Samples(service).Length == 441000, "Exactly ten-second resampled WAV is accepted");
service.ConfigureSqueak("original", null);
void Reject(string path, string name)
{
    try { service.ConfigureSqueak("custom", path); throw new Exception(name + " was accepted"); }
    catch (InvalidDataException) { Check(ReferenceEquals(original, Samples(service)), name + " rejected without losing original cache"); }
}
Reject(silent, "Missing file");
string longFile = Path.Combine(folder, "long.wav");
using (var writer = new WaveFileWriter(longFile, new WaveFormat(44100, 16, 1))) writer.Write(new byte[44100 * 11 * 2], 0, 44100 * 11 * 2);
Reject(longFile, "Over-ten-second audio");
string invalid = Path.Combine(folder, "invalid.wav"); File.WriteAllText(invalid, "This is not a sound."); Reject(invalid, "Invalid WAV");
string unsupported = Path.Combine(folder, "clip.ogg"); File.WriteAllText(unsupported, "x"); Reject(unsupported, "Unsupported extension");
string oversized = Path.Combine(folder, "large.wav"); using (var file = File.Create(oversized)) file.SetLength(20 * 1024 * 1024 + 1); Reject(oversized, "Over-20MB file");

string mp3 = Path.Combine(AppContext.BaseDirectory, "Fixtures", "duck.mp3");
service.ConfigureSqueak("custom", mp3);
Check(Samples(service).Length > 10000 && Samples(service).Length < 20000 && Samples(service).Any(x => x != 0), "Own-generated MP3 fixture decoded and cached");
custom = Samples(service);
service.PlaySqueak(); ReadSamples(128); service.PressSqueak(); service.ReleaseSqueak();
Check(ReadSamples(custom.Length + 256).SequenceEqual(custom.Concat(new short[256])), "Custom sound preview is interrupted by the latest press without duplicate release or backlog");
Console.WriteLine($"{checks} sound checks passed. Original SHA256: {Convert.ToHexString(SHA256.HashData(Bytes(original)))}");
