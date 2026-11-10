using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
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
        double dialogueEditP95Ms = 0, dialogueEditMaxMs = 0;
        try
        {
            await Task.Delay(500);
            pet.ValidateTrayForTest(); checks.Add("tray icon starts with packaged assets");
            using var original = SpriteAnimation.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "default-original.gif"));
            Require(original.IsAnimated && original.Frames.Count == 27, "original GIF keeps 27 animated frames", checks);
            Require(pet.CurrentFrame.PixelWidth <= 512 && pet.CurrentFrame.PixelHeight <= 512, "default sprite resolution bounded", checks);
            pet.ShowSettings(); await Task.Delay(250);
            double windowScale = DesktopService.GetWindowScale(new WindowInteropHelper(pet).Handle);
            Require(pet.Title == $"弹弹桌宠 · Tantan · {pet.Settings.InstanceName}" &&
                pet.SettingsPanel!.Title == $"弹弹桌宠 · Tantan · {pet.Settings.InstanceName} · 设置", "application and settings identify the selected Tantan character", checks);
            if (pet.Coordinator is not null)
                Require(MultiPetSmokeTest.IsSelectorBoundAndVisible(pet, 1), "the initial character selector renders its name and binds the selected window", checks);
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
            var settleWatch = Stopwatch.StartNew();
            do { await Task.Delay(50); }
            while ((Math.Abs(squash.ScaleX - 1) >= .015 || Math.Abs(squash.ScaleY - 1) >= .015) && settleWatch.Elapsed.TotalSeconds < 2);
            Require(Math.Abs(squash.ScaleX - 1) < .015 && Math.Abs(squash.ScaleY - 1) < .015,
                $"release settles to original shape (x={squash.ScaleX:F4}, y={squash.ScaleY:F4})", checks);
            pet.Kick(); double startX = pet.Physics.X, startY = pet.Physics.Y;
            await Task.Delay(400);
            Require(Math.Abs(pet.Physics.X - startX) + Math.Abs(pet.Physics.Y - startY) > 10, "physics moves actual pet window", checks);
            Require(Math.Abs(pet.Physics.Angle) > .01, "physics rotates pet", checks);
            pet.Settings.PhysicsEnabled = false; pet.ApplySettings();
            Require(pet.Physics.Angle == 0 && pet.Physics.VelocityX == 0 && pet.Physics.VelocityY == 0, "fixed mode stops and stands upright", checks);
            var characterSize = (System.Windows.Controls.Slider)pet.SettingsPanel.FindName("SizeSlider");
            characterSize.Value = 320; await Task.Delay(100);
            Require(Math.Abs(image.ActualHeight - 320) < 1, "size slider resizes sprite preserving aspect ratio", checks);
            pet.SayRandom(); await Task.Delay(100);
            Require(pet.BubblePanel.IsVisible, "random dialogue displays actual thought bubble", checks);
            Snapshot((FrameworkElement)pet.BubblePanel.Content, Path.Combine(output, "bubble.png"));
            var presets = (System.Windows.Controls.TextBox)pet.SettingsPanel.FindName("PresetPhrasesBox");
            var custom = (System.Windows.Controls.TextBox)pet.SettingsPanel.FindName("PhrasesBox");
            ((System.Windows.Controls.TabControl)pet.SettingsPanel.FindName("SettingsTabs")).SelectedIndex = 2;
            await Task.Delay(80);
            int applyCount = pet.FullSettingsApplyCount;
            presets.Text = string.Join(Environment.NewLine, Enumerable.Repeat("预设编辑测试", 150));
            var editTimes = new List<double>();
            for (int i = 0; i < 200; i++)
            {
                var watch = Stopwatch.StartNew();
                presets.Select(presets.Text.Length, 0); presets.SelectedText = "耶";
                editTimes.Add(watch.Elapsed.TotalMilliseconds);
                if (i % 20 == 0) await Task.Delay(1);
            }
            custom.Text = "自定义编辑测试";
            Require(pet.FullSettingsApplyCount == applyCount, "continuous text edits avoid audio and full-window settings reconfiguration", checks);
            Require(pet.Settings.PresetPhrases == presets.Text && pet.Settings.CustomPhrases == custom.Text,
                "continuous text edits retain the latest preset and custom text", checks);
            editTimes.Sort();
            dialogueEditP95Ms = editTimes[(int)Math.Ceiling(editTimes.Count * .95) - 1];
            dialogueEditMaxMs = editTimes[^1];
            presets.Text = "昨日一去不复回"; custom.Text = "";
            pet.SayRandom();
            Require(pet.BubblePanel.CurrentText == "昨日一去不复回", "editing presets replaces the random dialogue pool immediately", checks);
            var fonts = (System.Windows.Controls.ComboBox)pet.SettingsPanel.FindName("FontCombo");
            fonts.SelectedItem = fonts.Items.OfType<FontFamily>().First(font => font.Source == "Arial");
            ((System.Windows.Controls.Slider)pet.SettingsPanel.FindName("FontSizeSlider")).Value = 24;
            await Task.Delay(80);
            Require(pet.BubblePanel.FontFamily.Source == "Arial" && pet.BubblePanel.CurrentFontSize == 24, "font and size controls update the visible bubble", checks);
            Snapshot((FrameworkElement)pet.BubblePanel.Content, Path.Combine(output, "bubble-large.png"));
            presets.Text = "耶！"; custom.Text = ""; pet.SayRandom();
            characterSize.Value = 220;
            var bubbleScale = (System.Windows.Controls.Slider)pet.SettingsPanel.FindName("BubbleScaleSlider");
            var fontSize = (System.Windows.Controls.Slider)pet.SettingsPanel.FindName("FontSizeSlider");
            bubbleScale.Value = 1; fontSize.Value = 16;
            await Task.Delay(80);
            double bubbleWidth = pet.BubblePanel.Width - 4;
            characterSize.Value = 330; await Task.Delay(80);
            Require(Math.Abs(pet.BubblePanel.CurrentFontSize - 24) < .01 && Math.Abs(fontSize.Value - 24) < .01 &&
                Math.Abs((pet.BubblePanel.Width - 4) / bubbleWidth - 1.5) < .02,
                "character resize synchronizes whole bubble, actual font and font slider", checks);
            bubbleScale.Value = 1.5; await Task.Delay(80);
            Require(Math.Abs(pet.BubblePanel.CurrentFontSize - 36) < .01 && Math.Abs(fontSize.Value - 36) < .01 &&
                Math.Abs(pet.BubblePanel.RequestedVisualScale - 2.25) < .01,
                "separate bubble resize scales artwork and synchronizes font size", checks);
            var fontWeight = (System.Windows.Controls.ComboBox)pet.SettingsPanel.FindName("FontWeightCombo");
            fontWeight.SelectedItem = fontWeight.Items.OfType<System.Windows.Controls.ComboBoxItem>().First(item => (string)item.Tag == "700");
            Require(pet.BubblePanel.CurrentFontWeight == 700 && pet.Settings.BubbleFontWeight == 700,
                "font weight control updates the visible bubble and saved preference", checks);
            characterSize.Value = 480; bubbleScale.Value = 2; fontSize.Value = fontSize.Maximum;
            presets.Text = new string('耶', 110); pet.SayRandom(); await Task.Delay(100);
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)pet.Physics.X, (int)pet.Physics.Y)).WorkingArea;
            Require(pet.BubblePanel.ActualWidth * windowScale <= screen.Width && pet.BubblePanel.ActualHeight * windowScale <= screen.Height,
                "largest character, bubble and long phrase remain fitted within the monitor", checks);
            Snapshot((FrameworkElement)pet.BubblePanel.Content, Path.Combine(output, "bubble-fitted.png"));
            characterSize.Value = 220; bubbleScale.Value = 1; fontSize.Value = 24;
            presets.Text = "昨日一去不复回";
            var randomSpeed = (System.Windows.Controls.CheckBox)pet.SettingsPanel.FindName("RandomSpeedCheck");
            var speed = (System.Windows.Controls.Slider)pet.SettingsPanel.FindName("SpeedSlider");
            speed.Value = 400; randomSpeed.IsChecked = true;
            var kicks = new List<double>();
            for (int i = 0; i < 30; i++) { pet.Kick(); kicks.Add(Math.Sqrt(pet.Physics.VelocityX * pet.Physics.VelocityX + pet.Physics.VelocityY * pet.Physics.VelocityY) / windowScale); }
            Require(kicks.All(value => value >= 25 && value <= 400) && kicks.Select(value => Math.Round(value)).Distinct().Count() > 10,
                "random-speed option varies each kick within the selected upper bound", checks);
            randomSpeed.IsChecked = false; pet.Kick();
            Require(Math.Abs(Math.Sqrt(pet.Physics.VelocityX * pet.Physics.VelocityX + pet.Physics.VelocityY * pet.Physics.VelocityY) / windowScale - 400) < .01,
                "disabling random speed restores the selected fixed kick magnitude", checks);
            pet.Settings.PhysicsEnabled = false; pet.ApplySettings(); pet.SettingsPanel.Reload();
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
            bool privateUpdates = Environment.GetCommandLineArgs().Contains("--verify-private-updates");
            if (Environment.GetCommandLineArgs().Contains("--verify-updates") || privateUpdates)
            {
                var update = await pet.SettingsPanel.CheckForUpdatesAsync();
                Require(privateUpdates ? update.Status == UpdateStatus.NoRelease && update.Message.Contains("私有") : update.Status == UpdateStatus.UpToDate,
                    privateUpdates ? "private GitHub access guides users to the signed-in release page" : "actual GitHub latest release matches installed version", checks);
                ((System.Windows.Controls.TabControl)pet.SettingsPanel.FindName("SettingsTabs")).SelectedIndex = 3;
                await Task.Delay(80);
                Snapshot((FrameworkElement)pet.SettingsPanel.Content, Path.Combine(output, "update-settings.png"));
            }
            Snapshot((FrameworkElement)pet.Content, Path.Combine(output, "pet.png"));
            pet.Settings.Size = 220; pet.ApplySettings(); pet.SettingsPanel.Reload(); await Task.Delay(100);
            Snapshot((FrameworkElement)pet.SettingsPanel.Content, Path.Combine(output, "settings.png"));
            if (pet.Coordinator is not null) checks.AddRange(await MultiPetSmokeTest.Run(pet.Coordinator, output));
            File.WriteAllText(Path.Combine(output, "smoke-result.json"), JsonSerializer.Serialize(new { passed = true, checks, dialogueEditP95Ms, dialogueEditMaxMs, memoryMB = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576.0 }, new JsonSerializerOptions { WriteIndented = true }));
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
