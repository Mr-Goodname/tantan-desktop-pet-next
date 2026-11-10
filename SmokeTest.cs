using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
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
            VerifyPointerDialogue(pet, output, checks);
            VerifyBubblePlacement(pet, output, checks);
            checks.AddRange(await GifAudioSmokeTest.Run(pet, output));
            checks.AddRange(await FullscreenSmokeTest.Run(pet, output));
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
    private static void VerifyBubblePlacement(PetWindow pet, string output, List<string> checks)
    {
        var saved = pet.Settings.Copy();
        var position = new Point(pet.Physics.X, pet.Physics.Y);
        var velocity = new Point(pet.Physics.VelocityX, pet.Physics.VelocityY);
        double angle = pet.Physics.Angle, angularVelocity = pet.Physics.AngularVelocity;
        bool bubbleWasVisible = pet.BubblePanel.IsVisible;
        string previousText = pet.BubblePanel.CurrentText;
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var deadlineField = typeof(PetWindow).GetField("_bubbleDeadline", fields)!;
        var lifetimeField = typeof(PetWindow).GetField("_bubbleUntil", fields)!;
        double previousDeadline = (double)deadlineField.GetValue(pet)!, previousLifetime = (double)lifetimeField.GetValue(pet)!;
        try
        {
            pet.Settings.AudioEnabled = false; pet.Settings.SqueakVolume = 0; pet.Settings.PhysicsEnabled = false;
            pet.Settings.BubblesEnabled = false; pet.Settings.Size = 160; pet.Settings.BubbleScale = 1;
            pet.Settings.BubbleFontSize = 13.5; pet.Settings.BubbleFontWeight = 400;
            pet.Settings.PresetPhrases = "看我看我~"; pet.Settings.CustomPhrases = "";
            pet.ApplySettings();
            var screen = DesktopService.GetMonitorBounds(new WindowInteropHelper(pet).Handle);
            pet.Physics.X = (screen.Left + screen.Right) / 2; pet.Physics.Y = screen.Top;
            pet.RefreshAfterPeerCollision(); pet.SayRandom();
            var bubble = pet.BubblePanel;
            const string lines = "第一行：你好呀\n第二行：欧耶耶~";
            bubble.Say(lines); pet.RefreshAfterPeerCollision();
            var host = (System.Windows.Controls.Border)bubble.Content;
            var surface = (ThoughtBubbleSurface)host.Child;
            var text = (System.Windows.Controls.TextBlock)surface.Child;
            var bubbleHandle = new WindowInteropHelper(bubble).Handle;
            Require(GetSmokeWindowRect(bubbleHandle, out var below), "the lower bubble has a real native window rectangle", checks);
            var natural = surface.NaturalSize;
            double width = bubble.ActualWidth, height = bubble.ActualHeight, scale = bubble.CurrentVisualScale;
            Require(bubble.PointsUp && below.Top > pet.Physics.Y && below.Bottom <= screen.Bottom - 7,
                "the top screen boundary places the real bubble below the character with its dots pointing up", checks);
            Require(((Transform)text.TransformToAncestor(bubble)).Value.M22 > 0 && text.Text == lines &&
                surface.ContainsTextBounds(surface.TextLayoutBounds),
                "the lower bubble keeps its glyphs upright and its original line order inside the reflected paper", checks);
            VerifyBubbleRaster(bubble, true, Path.Combine(output, "bubble-below-artwork.png"), checks);
            SnapshotPetAndBubble(pet, Path.Combine(output, "bubble-below-character.png"));
            pet.Physics.Y = screen.Bottom; pet.RefreshAfterPeerCollision();
            Require(GetSmokeWindowRect(bubbleHandle, out var above), "the restored upper bubble has a real native window rectangle", checks);
            Require(!bubble.PointsUp && above.Bottom < pet.Physics.Y && above.Top >= screen.Top + 7,
                "moving the character away from the top boundary restores an upper bubble with its dots pointing down", checks);
            Require(bubble.ActualWidth == width && bubble.ActualHeight == height && surface.NaturalSize == natural &&
                bubble.CurrentVisualScale == scale && above.Right - above.Left == below.Right - below.Left &&
                above.Bottom - above.Top == below.Bottom - below.Top && ((Transform)text.TransformToAncestor(bubble)).Value.M22 > 0,
                "switching bubble direction preserves native size, full artwork scale and upright text", checks);
            VerifyBubbleRaster(bubble, false, Path.Combine(output, "bubble-above-artwork.png"), checks);
            SnapshotPetAndBubble(pet, Path.Combine(output, "bubble-above-character.png"));
        }
        finally
        {
            foreach (var property in typeof(PetSettings).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public).Where(property => property.CanWrite))
                property.SetValue(pet.Settings, property.GetValue(saved));
            pet.ApplySettings(); pet.Physics.X = position.X; pet.Physics.Y = position.Y;
            pet.Physics.VelocityX = velocity.X; pet.Physics.VelocityY = velocity.Y;
            pet.Physics.Angle = angle; pet.Physics.AngularVelocity = angularVelocity;
            if (bubbleWasVisible) pet.BubblePanel.Say(previousText); else pet.BubblePanel.Hide();
            pet.RefreshAfterPeerCollision(); pet.SettingsPanel?.Reload();
            deadlineField.SetValue(pet, previousDeadline); lifetimeField.SetValue(pet, previousLifetime);
        }
    }
    private static void VerifyBubbleRaster(BubbleWindow bubble, bool pointsUp, string path, List<string> checks)
    {
        var content = (FrameworkElement)bubble.Content; content.UpdateLayout();
        int width = (int)Math.Ceiling(content.ActualWidth), height = (int)Math.Ceiling(content.ActualHeight);
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); target.Render(content);
        var pixels = new byte[width * height * 4]; target.CopyPixels(pixels, width * 4, 0);
        var bandWidths = new List<int>(); int currentWidth = 0; bool edgeInk = false;
        for (int y = 0; y < height; y++)
        {
            int left = width, right = -1;
            for (int x = 0; x < width; x++)
                if (pixels[(y * width + x) * 4 + 3] != 0)
                {
                    left = Math.Min(left, x); right = x;
                    edgeInk |= x == 0 || x == width - 1 || y == 0 || y == height - 1;
                }
            if (right >= left) currentWidth = Math.Max(currentWidth, right - left + 1);
            else if (currentWidth > 0) { bandWidths.Add(currentWidth); currentWidth = 0; }
        }
        if (currentWidth > 0) bandWidths.Add(currentWidth);
        var directedBands = pointsUp ? bandWidths.AsEnumerable().Reverse().ToArray() : bandWidths.ToArray();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target)); using var file = File.Create(path); encoder.Save(file);
        Require(directedBands.Length == 3 && directedBands[0] > directedBands[1] * 2 && directedBands[1] > directedBands[2],
            "the real " + (pointsUp ? "lower" : "upper") + " bubble PNG orders the paper, large dot and small dot toward the character (band widths: " +
                string.Join(",", bandWidths) + ")", checks);
        Require(!edgeInk, "the real " + (pointsUp ? "lower" : "upper") + " bubble PNG retains every stroke without touching a clipping edge", checks);
    }
    private static void SnapshotPetAndBubble(PetWindow pet, string path)
    {
        if (!GetSmokeWindowRect(new WindowInteropHelper(pet).Handle, out var petRect) ||
            !GetSmokeWindowRect(new WindowInteropHelper(pet.BubblePanel).Handle, out var bubbleRect))
            throw new InvalidOperationException("Native pet and bubble rectangles are unavailable for the placement preview.");
        int left = Math.Min(petRect.Left, bubbleRect.Left), top = Math.Min(petRect.Top, bubbleRect.Top);
        int right = Math.Max(petRect.Right, bubbleRect.Right), bottom = Math.Max(petRect.Bottom, bubbleRect.Bottom);
        var petContent = (FrameworkElement)pet.Content;
        var bubbleContent = (FrameworkElement)pet.BubblePanel.Content;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(246, 248, 242)), null, new Rect(0, 0, right - left, bottom - top));
            drawing.DrawRectangle(new VisualBrush(petContent)
                { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, petContent.ActualWidth, petContent.ActualHeight) }, null,
                new Rect(petRect.Left - left, petRect.Top - top, petRect.Right - petRect.Left, petRect.Bottom - petRect.Top));
            drawing.DrawRectangle(new VisualBrush(bubbleContent)
                { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, bubbleContent.ActualWidth, bubbleContent.ActualHeight) }, null,
                new Rect(bubbleRect.Left - left, bubbleRect.Top - top, bubbleRect.Right - bubbleRect.Left, bubbleRect.Bottom - bubbleRect.Top));
        }
        var target = new RenderTargetBitmap(right - left, bottom - top, 96, 96, PixelFormats.Pbgra32); target.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target)); using var file = File.Create(path); encoder.Save(file);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SmokeWindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    private static extern bool GetSmokeWindowRect(IntPtr handle, out SmokeWindowRect rect);
    private static void VerifyPointerDialogue(PetWindow pet, string output, List<string> checks)
    {
        var saved = pet.Settings.Copy();
        var position = new Point(pet.Physics.X, pet.Physics.Y);
        bool bubbleWasVisible = pet.BubblePanel.IsVisible;
        string previousText = pet.BubblePanel.CurrentText;
        var clockField = typeof(PetWindow).GetField("_clock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var deadlineField = typeof(PetWindow).GetField("_bubbleDeadline", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var lifetimeField = typeof(PetWindow).GetField("_bubbleUntil", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        double previousDeadline = (double)deadlineField.GetValue(pet)!, previousLifetime = (double)lifetimeField.GetValue(pet)!;
        try
        {
            pet.Settings.AudioEnabled = false; pet.Settings.SqueakVolume = 0;
            pet.Settings.PhysicsEnabled = false; pet.Settings.BubblesEnabled = true; pet.Settings.ClickShowsDialogue = true;
            pet.Settings.PresetPhrases = "点我一下，欧耶耶~"; pet.Settings.CustomPhrases = "";
            pet.ApplySettings(); pet.BubblePanel.Hide();
            PointerPress(pet); PointerRelease(pet);
            Require(pet.BubblePanel.IsVisible && pet.BubblePanel.CurrentText == "点我一下，欧耶耶~",
                "routed mouse press and release show the clicked instance's random dialogue", checks);
            Snapshot((FrameworkElement)pet.BubblePanel.Content, Path.Combine(output, "bubble-click.png"));
            pet.BubblePanel.Hide(); PointerRelease(pet);
            Require(!pet.BubblePanel.IsVisible, "a duplicate mouse release cannot trigger a second click dialogue", checks);

            pet.Settings.ClickShowsDialogue = false; pet.BubblePanel.Hide();
            PointerPress(pet); PointerRelease(pet);
            Require(!pet.BubblePanel.IsVisible, "disabling click dialogue keeps a normal click from opening a bubble", checks);
            pet.Settings.ClickShowsDialogue = true; pet.Settings.BubblesEnabled = false;
            PointerPress(pet); PointerRelease(pet);
            Require(pet.BubblePanel.IsVisible && pet.BubblePanel.CurrentText == "点我一下，欧耶耶~",
                "click dialogue remains available when automatic dialogue is disabled", checks);
            pet.Settings.BubblesEnabled = true; pet.BubblePanel.Hide();
            PointerPress(pet);
            var grabPointField = typeof(PetWindow).GetField("_grabPoint", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var grabbedPoint = (System.Drawing.Point)grabPointField.GetValue(pet)!;
            pet.MovePointerForTest(new System.Drawing.Point(grabbedPoint.X + 12, grabbedPoint.Y));
            var draggedField = typeof(PetWindow).GetField("_dragged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Require((bool)draggedField.GetValue(pet)!, "the shared pointer movement path marks a deterministic drag beyond the threshold", checks);
            PointerRelease(pet);
            Require(!pet.BubblePanel.IsVisible, "dragging beyond the pointer threshold does not trigger click dialogue", checks);
            PointerPress(pet); pet.ReleaseMouseCapture();
            Require(!pet.BubblePanel.IsVisible, "losing mouse capture without a release is not treated as a click", checks);

            var clock = (Stopwatch)clockField.GetValue(pet)!;
            pet.Settings.BubbleInterval = 60; pet.ApplySettings(); pet.SettingsPanel!.Reload();
            var intervalSlider = (System.Windows.Controls.Slider)pet.SettingsPanel.FindName("BubbleSlider");
            var automaticDialogue = (System.Windows.Controls.CheckBox)pet.SettingsPanel.FindName("BubblesCheck");
            double slowDeadline = (double)deadlineField.GetValue(pet)!;
            double beforeChange = clock.Elapsed.TotalSeconds;
            intervalSlider.Value = 1;
            double afterChange = clock.Elapsed.TotalSeconds, changedDeadline = (double)deadlineField.GetValue(pet)!;
            Require(pet.Settings.BubbleInterval == 1 && changedDeadline >= beforeChange + 1 - .000001 &&
                changedDeadline <= afterChange + 1.3 + .000001 && changedDeadline < slowDeadline,
                "changing the dialogue interval slider from 60 to 1 immediately schedules a 1–1.3-second delay", checks);

            pet.Settings.BubbleFontSize = pet.Settings.BubbleFontSize < 31 ? pet.Settings.BubbleFontSize + 1 : pet.Settings.BubbleFontSize - 1;
            pet.ApplySettings();
            Require((double)deadlineField.GetValue(pet)! == changedDeadline,
                "applying an unrelated dialogue font setting leaves the scheduled dialogue time unchanged", checks);
            pet.Settings.Size = pet.Settings.Size < 470 ? pet.Settings.Size + 10 : pet.Settings.Size - 10;
            pet.ApplySettings();
            Require((double)deadlineField.GetValue(pet)! == changedDeadline,
                "applying an unrelated character size setting leaves the scheduled dialogue time unchanged", checks);
            pet.SettingsPanel.Reload();

            deadlineField.SetValue(pet, clock.Elapsed.TotalSeconds - 1);
            beforeChange = clock.Elapsed.TotalSeconds; automaticDialogue.IsChecked = false;
            afterChange = clock.Elapsed.TotalSeconds; changedDeadline = (double)deadlineField.GetValue(pet)!;
            Require(!pet.Settings.BubblesEnabled && !pet.BubblePanel.IsVisible &&
                changedDeadline >= beforeChange + 1 - .000001 && changedDeadline <= afterChange + 1.3 + .000001,
                "turning automatic dialogue off hides its bubble and immediately replaces an expired deadline", checks);
            deadlineField.SetValue(pet, clock.Elapsed.TotalSeconds - 1);
            beforeChange = clock.Elapsed.TotalSeconds; automaticDialogue.IsChecked = true;
            afterChange = clock.Elapsed.TotalSeconds; changedDeadline = (double)deadlineField.GetValue(pet)!;
            Require(pet.Settings.BubblesEnabled && changedDeadline >= beforeChange + 1 - .000001 &&
                changedDeadline <= afterChange + 1.3 + .000001,
                "turning automatic dialogue back on starts a fresh 1–1.3-second delay", checks);

            foreach (double interval in new[] { 1d, 60d })
            {
                pet.Settings.BubbleInterval = interval;
                bool bounded = true;
                for (int i = 0; i < 20; i++)
                {
                    double before = clock.Elapsed.TotalSeconds;
                    pet.SayRandom();
                    double after = clock.Elapsed.TotalSeconds, deadline = (double)deadlineField.GetValue(pet)!;
                    bounded &= deadline >= before + 1 - .000001 && deadline <= after + 60 + .000001;
                }
                Require(bounded, "twenty automatic dialogue delays stay inside 1–60 seconds at interval " + interval, checks);
            }
        }
        finally
        {
            if (pet.IsMouseCaptured) pet.ReleaseMouseCapture();
            foreach (var property in typeof(PetSettings).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public).Where(property => property.CanWrite))
                property.SetValue(pet.Settings, property.GetValue(saved));
            pet.ApplySettings(); pet.Physics.X = position.X; pet.Physics.Y = position.Y;
            pet.RefreshAfterPeerCollision(); pet.SettingsPanel?.Reload();
            if (bubbleWasVisible) pet.BubblePanel.Say(previousText); else pet.BubblePanel.Hide();
            deadlineField.SetValue(pet, previousDeadline); lifetimeField.SetValue(pet, previousLifetime);
        }
    }
    private static void PointerPress(PetWindow pet) =>
        ((UIElement)pet.FindName("PetImage")).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
    private static void PointerRelease(PetWindow pet) =>
        pet.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
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
