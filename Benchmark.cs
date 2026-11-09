using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BonbonPet;

internal static class Benchmark
{
    internal static async Task Run(PetWindow pet, string? directory)
    {
        var output = directory ?? Path.Combine(AppContext.BaseDirectory, "test-artifacts");
        Directory.CreateDirectory(output);
        var results = new List<object>();
        pet.Settings.BubblesEnabled = false; pet.Settings.AudioEnabled = false; pet.ApplySettings();
        await Task.Delay(1500);
        await Sample("fixed GIF, audio off");
        pet.Settings.AudioEnabled = true; pet.ApplySettings(); await Task.Delay(500);
        await Sample("fixed GIF, system audio reacting");
        pet.Kick(); await Sample("rotation, motion, audio reacting");
        pet.Settings.SqueakSound = "duck"; pet.Settings.SqueakVolume = .01; pet.ApplySettings();
        await Sample("rotation and repeated press/release with warmed duck output", clicks: true);
        File.WriteAllText(Path.Combine(output, "performance.json"), JsonSerializer.Serialize(new { fps = pet.Settings.Fps, logicalProcessors = Environment.ProcessorCount, measurements = results }, new JsonSerializerOptions { WriteIndented = true }));
        pet.Exit();

        async Task Sample(string scenario, bool clicks = false)
        {
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
            pet.BeginFrameMeasurement();
            if (clicks)
            {
                for (int i = 0; i < 12; i++)
                {
                    pet.BeginPressFeedback(); await Task.Delay(100);
                    pet.EndPressFeedback(); await Task.Delay(150);
                }
            }
            else await Task.Delay(3000);
            process.Refresh();
            var frames = pet.EndFrameMeasurement();
            var sorted = frames.OrderBy(x => x).ToArray();
            double fps = frames.Length > 0 ? 1000 * frames.Length / frames.Sum() : 0;
            double p95 = sorted.Length > 0 ? sorted[(int)Math.Floor((sorted.Length - 1) * .95)] : 0;
            double oneCore = (process.TotalProcessorTime - cpu).TotalSeconds / watch.Elapsed.TotalSeconds * 100;
            results.Add(new { scenario, effectiveFps = fps, frameIntervalP95Ms = p95, maximumFrameIntervalMs = sorted.LastOrDefault(), cpuPercentOfOneCore = oneCore, cpuPercentOfMachine = oneCore / Environment.ProcessorCount, workingSetMB = process.WorkingSet64 / 1048576.0, privateBytesMB = process.PrivateMemorySize64 / 1048576.0 });
        }
    }
}
