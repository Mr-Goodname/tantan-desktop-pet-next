using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BonbonPet.Controls;
using BonbonPet.Core;
using BonbonPet.Services;

namespace BonbonPet;

internal static class SmokeTest
{
    internal static async Task Run(PetWindow pet, string? directory)
    {
        string output = directory ?? Path.Combine(AppContext.BaseDirectory, "test-artifacts");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        try
        {
            await Task.Delay(500);
            pet.ValidateTrayForTest(); checks.Add("tray icon starts with packaged assets");
            using var original = SpriteAnimation.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "default-original.gif"));
            Require(original.IsAnimated && original.Frames.Count == 27, "original GIF keeps 27 animated frames", checks);
            Require(pet.CurrentFrame.PixelWidth <= 512 && pet.CurrentFrame.PixelHeight <= 512, "default sprite resolution bounded", checks);
            pet.ShowSettings(); await Task.Delay(250);
            Require(pet.Title == "弹弹桌宠" && pet.SettingsPanel!.Title == "弹弹桌宠 · 设置", "application and settings use updated name", checks);
            Snapshot((FrameworkElement)pet.SettingsPanel!.Content, Path.Combine(output, "settings.png"));
            var sounds = (System.Windows.Controls.ComboBox)pet.SettingsPanel.FindName("SoundCombo");
            sounds.SelectedIndex = 1; Require(pet.Settings.SqueakSound == (AudioService.HasDuckAssets ? "duck" : "original"), "duck selection uses supplied assets or synthesized fallback", checks);
            sounds.SelectedIndex = 0; Require(pet.Settings.SqueakSound == "original", "original sound retained and selectable", checks);
            var elastic = (System.Windows.Controls.Slider)pet.SettingsPanel.FindName("SqueezeSlider");
            pet.Settings.AudioEnabled = false; pet.SettingsPanel.Reload();
            pet.Settings.SqueakVolume = 0;
            elastic.Value = 0; pet.ApplySettings(); pet.Squeeze(); await Task.Delay(80);
            var squeezeTransform = (ScaleTransform)pet.FindName("Squash");
            Require(Math.Abs(squeezeTransform.ScaleX - 1) < .01, "zero elasticity disables squeeze deformation", checks);
            elastic.Value = 3; pet.Squeeze(); double maximum = 0;
            for (int i = 0; i < 8; i++) { await Task.Delay(15); maximum = Math.Max(maximum, Math.Abs(squeezeTransform.ScaleX - 1)); }
            Require(maximum > .18, "elasticity slider gives visibly stronger squash", checks);
            elastic.Value = 1.8;
            pet.Settings.SqueakVolume = 0; pet.ApplySettings(); pet.Squeeze(); await Task.Delay(45);
            var image = (System.Windows.Controls.Image)pet.FindName("PetImage");
            var squash = (ScaleTransform)pet.FindName("Squash");
            Require(Math.Abs(squash.ScaleX - 1) > .025, "squeeze changes shape", checks);
            Snapshot((FrameworkElement)pet.Content, Path.Combine(output, "squeeze.png"));
            await Task.Delay(1000);
            Require(Math.Abs(squash.ScaleX - 1) < .08, "squeeze settles back", checks);
            pet.BeginPressFeedback(); await Task.Delay(400);
            Require(squash.ScaleY < .76 && squash.ScaleX > 1.18, "held press keeps pet visibly compressed", checks);
            double heldScale = squash.ScaleY;
            Snapshot((FrameworkElement)pet.Content, Path.Combine(output, "pressed.png"));
            await Task.Delay(350);
            Require(Math.Abs(squash.ScaleY - heldScale) < .01, "long hold does not release or animate repeatedly", checks);
            pet.EndPressFeedback(); bool overshot = false;
            for (int i = 0; i < 14; i++) { await Task.Delay(20); overshot |= squash.ScaleY > 1.04; }
            Require(overshot, "mouse release gives visible spring overshoot", checks);
            await Task.Delay(900);
            Require(Math.Abs(squash.ScaleX - 1) < .015 && Math.Abs(squash.ScaleY - 1) < .015, "release settles to original shape", checks);
            pet.Kick(); double startX = pet.Physics.X, startY = pet.Physics.Y;
            await Task.Delay(400);
            Require(Math.Abs(pet.Physics.X - startX) + Math.Abs(pet.Physics.Y - startY) > 10, "physics moves actual pet window", checks);
            Require(Math.Abs(pet.Physics.Angle) > .01, "physics rotates pet", checks);
            pet.Settings.PhysicsEnabled = false; pet.ApplySettings();
            Require(pet.Physics.Angle == 0 && pet.Physics.VelocityX == 0 && pet.Physics.VelocityY == 0, "fixed mode stops and stands upright", checks);
            pet.Settings.Size = 320; pet.ApplySettings(); await Task.Delay(100);
            Require(Math.Abs(image.ActualHeight - 320) < 1, "size slider resizes sprite preserving aspect ratio", checks);
            pet.SayRandom(); await Task.Delay(100);
            Require(pet.BubblePanel.IsVisible, "random dialogue displays actual thought bubble", checks);
            Snapshot((FrameworkElement)pet.BubblePanel.Content, Path.Combine(output, "bubble.png"));
            var presets = (System.Windows.Controls.TextBox)pet.SettingsPanel.FindName("PresetPhrasesBox");
            var custom = (System.Windows.Controls.TextBox)pet.SettingsPanel.FindName("PhrasesBox");
            presets.Text = "昨日一去不复回"; custom.Text = "";
            pet.SayRandom();
            Require(pet.BubblePanel.CurrentText == "昨日一去不复回", "editing presets replaces the random dialogue pool immediately", checks);
            var fonts = (System.Windows.Controls.ComboBox)pet.SettingsPanel.FindName("FontCombo");
            fonts.SelectedItem = fonts.Items.OfType<FontFamily>().First(font => font.Source == "Arial");
            ((System.Windows.Controls.Slider)pet.SettingsPanel.FindName("FontSizeSlider")).Value = 24;
            await Task.Delay(80);
            Require(pet.BubblePanel.FontFamily.Source == "Arial" && pet.BubblePanel.CurrentFontSize == 24, "font and size controls update the visible bubble", checks);
            Snapshot((FrameworkElement)pet.BubblePanel.Content, Path.Combine(output, "bubble-large.png"));
            custom.Text = "哦！";
            Require(PhraseBook.GetPhrases(pet.Settings).SequenceEqual(new[] { "昨日一去不复回", "哦！" }), "custom dialogue remains mixed with edited presets", checks);
            presets.Text = ""; custom.Text = ""; pet.SayRandom();
            Require(!pet.BubblePanel.IsVisible, "empty preset and custom editors safely disable random dialogue", checks);
            ((System.Windows.Controls.Button)pet.SettingsPanel.FindName("RestorePresetButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Require(pet.Settings.PresetPhrases == null && PhraseBook.GetPhrases(pet.Settings).Length == PhraseBook.Phrases.Length, "restore button recovers all lyric presets", checks);
            pet.SayRandom();
            ((System.Windows.Controls.TabControl)pet.SettingsPanel.FindName("SettingsTabs")).SelectedIndex = 2;
            await Task.Delay(80);
            ((System.Windows.Controls.ScrollViewer)pet.SettingsPanel.FindName("DialogueScroll")).ScrollToVerticalOffset(230);
            await Task.Delay(80);
            Snapshot((FrameworkElement)pet.SettingsPanel.Content, Path.Combine(output, "dialogue-settings.png"));
            if (Environment.GetCommandLineArgs().Contains("--verify-updates"))
            {
                var update = await pet.SettingsPanel.CheckForUpdatesAsync();
                Require(update.Status == UpdateStatus.UpToDate, "actual GitHub latest release matches installed version", checks);
                ((System.Windows.Controls.TabControl)pet.SettingsPanel.FindName("SettingsTabs")).SelectedIndex = 3;
                await Task.Delay(80);
                Snapshot((FrameworkElement)pet.SettingsPanel.Content, Path.Combine(output, "update-settings.png"));
            }
            Snapshot((FrameworkElement)pet.Content, Path.Combine(output, "pet.png"));
            pet.Settings.Size = 220; pet.ApplySettings(); pet.SettingsPanel.Reload(); await Task.Delay(100);
            Snapshot((FrameworkElement)pet.SettingsPanel.Content, Path.Combine(output, "settings.png"));
            File.WriteAllText(Path.Combine(output, "smoke-result.json"), JsonSerializer.Serialize(new { passed = true, checks, memoryMB = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576.0 }, new JsonSerializerOptions { WriteIndented = true }));
            pet.Exit();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "smoke-result.json"), JsonSerializer.Serialize(new { passed = false, checks, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            pet.Dispose(); System.Windows.Application.Current.Shutdown(1);
        }
    }
    private static void Require(bool pass, string name, List<string> checks) { if (!pass) throw new InvalidOperationException(name); checks.Add(name); }
    private static void Snapshot(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            if (element != System.Windows.Application.Current.MainWindow?.Content)
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(246, 248, 242)), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            drawing.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.Fill }, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
}
