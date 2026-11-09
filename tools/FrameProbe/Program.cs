using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BonbonPet.Services;

internal static class Program
{
 [STAThread] static void Main(string[] args)
 {
  var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
  var window = new Window { Width = 407, Height = 407, Left = -10000, Top = -10000, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false };
  var image = new Image { Width = 220, Height = 220, Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "preview.png"))), RenderTransformOrigin = new Point(.5,.5) };
  var rotation = new RotateTransform(); image.RenderTransform = rotation; window.Content = image;
  window.SourceInitialized += (_, _) => DesktopService.MakeToolWindow(new WindowInteropHelper(window).Handle);
  window.Show();
  _ = window.Dispatcher.InvokeAsync(async () =>
  {
   var output = args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory,"frame-result.json");
   var results = new List<object>();
   await Task.Delay(500);
   foreach (var mode in new[] { "render30bucket", "render60bucket", "renderRaw" })
   {
    results.Add(await Measure(mode, window, rotation));
   }
   var json = JsonSerializer.Serialize(new { renderingTier = RenderCapability.Tier >> 16, screenDpi = VisualTreeHelper.GetDpi(window).DpiScaleX, results }, new JsonSerializerOptions { WriteIndented = true });
   Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); File.WriteAllText(output,json); Console.WriteLine(json);
   window.Close(); app.Shutdown();
  });
  app.Run();
 }
 static async Task<object> Measure(string mode, Window window, RotateTransform rotation)
 {
  var clock = Stopwatch.StartNew(); var intervals = new List<double>(); double previous = 0, lastRenderStamp = -1; long previousBucket = -1;
  int fps = mode.Contains("60") ? 60 : 30;
  var hwnd = new WindowInteropHelper(window).Handle;
  using var process = Process.GetCurrentProcess(); var cpuStart = process.TotalProcessorTime;
  void Tick()
  {
   double now = clock.Elapsed.TotalSeconds;
   if (previous != 0) intervals.Add((now-previous)*1000); previous = now;
   rotation.Angle = now*45;
   DesktopService.PositionWindow(hwnd,-10000+now*150,-10000+Math.Sin(now)*40,false);
  }
  var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1.0/fps) };
  timer.Tick += (_,_) => Tick();
  EventHandler render = (_,e) =>
  {
   double stamp = ((RenderingEventArgs)e).RenderingTime.TotalSeconds;
   if (stamp == lastRenderStamp) return; lastRenderStamp = stamp;
   long bucket = (long)Math.Floor(stamp*fps+1e-6);
   if (mode != "renderRaw" && bucket == previousBucket) return;
   previousBucket = bucket;
   Tick();
  };
  if (mode.StartsWith("timer")) timer.Start(); else CompositionTarget.Rendering += render;
  await Task.Delay(5000);
  timer.Stop(); CompositionTarget.Rendering -= render;
  double elapsed = clock.Elapsed.TotalSeconds; process.Refresh();
  var ordered = intervals.Order().ToArray();
  double Q(double fraction) => ordered.Length == 0 ? 0 : ordered[(int)Math.Round((ordered.Length-1)*fraction)];
  return new { mode, callbacks = intervals.Count+1, effectiveFps = intervals.Count/Math.Max(.01,intervals.Sum()/1000), intervalMedianMs = Q(.5), intervalP95Ms = Q(.95), maxIntervalMs = Q(1), cpuOfOneCore = (process.TotalProcessorTime-cpuStart).TotalSeconds/elapsed*100, workingSetMB = process.WorkingSet64/1048576.0 };
 }
}
