using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using BonbonPet.Core;

namespace BonbonPet;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => { Log(args.Exception); MessageBox.Show("运行发生错误，详情已写入本地 error.log。\n" + args.Exception.Message, "弹弹桌宠"); args.Handled = true; Shutdown(1); };
        bool benchmark = e.Args.Contains("--benchmark");
        bool test = e.Args.Contains("--self-test") || benchmark;
        if (!test)
        {
            _singleInstance = new Mutex(true, "Local\\BonbonPet.Desktop.v1", out bool created);
            if (!created) { MessageBox.Show("弹弹桌宠已在运行。双击任务栏通知区域的桌宠图标可打开设置。", "弹弹桌宠"); Shutdown(); return; }
        }
        try
        {
            var settings = test ? new PetSettings { HideOnFullscreen = false } : PetSettings.Load();
            bool first = settings.FirstRun; settings.FirstRun = false;
            var pet = new PetWindow(settings, test); MainWindow = pet; pet.Show();
            if (benchmark) _ = Benchmark.Run(pet, e.Args.SkipWhile(x => x != "--benchmark").Skip(1).FirstOrDefault());
            else if (test) _ = SmokeTest.Run(pet, e.Args.SkipWhile(x => x != "--self-test").Skip(1).FirstOrDefault());
            else if (first || e.Args.Contains("--settings")) pet.ShowSettings();
        }
        catch (Exception ex) { Log(ex); MessageBox.Show("启动失败：\n" + ex.Message, "弹弹桌宠"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { if (MainWindow is PetWindow pet) pet.Dispose(); _singleInstance?.Dispose(); base.OnExit(e); }
    internal static void Log(Exception ex) { try { Directory.CreateDirectory(PetSettings.DataDirectory); File.AppendAllText(Path.Combine(PetSettings.DataDirectory, "error.log"), DateTimeOffset.Now + " " + ex + Environment.NewLine); } catch { } }
}
