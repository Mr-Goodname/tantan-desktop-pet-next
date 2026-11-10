using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BonbonPet.Core;
using BonbonPet.Services;
using Forms = System.Windows.Forms;

namespace BonbonPet;

internal static class MultiPetSmokeTest
{
    internal static async Task<List<string>> Run(PetCoordinator coordinator, string output)
    {
        var checks = new List<string>();
        var primary = coordinator.Instances[0];
        var original = primary.Settings.Copy();
        var primaryPosition = new Point(primary.Physics.X, primary.Physics.Y);
        PetWindow? second = null, third = null, uiCreated = null;
        Directory.CreateDirectory(output);
        try
        {
            Require(coordinator.Instances.Count == 1, "multi-pet checks start with the primary testing instance", checks);
            primary.Settings.AudioEnabled = false; primary.Settings.SqueakVolume = 0;
            primary.Settings.PhysicsEnabled = false; primary.Settings.HideOnFullscreen = false;
            primary.ApplySettings();
            second = coordinator.AddInstance(primary, showSettings: false);
            third = coordinator.AddInstance(primary, showSettings: false);
            Require(coordinator.Instances.Count == 3 && coordinator.Instances.All(pet => pet.IsVisible),
                "creating two additional profiles shows three independent desktop windows", checks);
            Require(!ReferenceEquals(primary.Settings, second.Settings) && !ReferenceEquals(second.Settings, third.Settings) &&
                coordinator.Instances.Select(pet => pet.Settings.InstanceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3,
                "each created window owns separate settings and a unique instance identity", checks);
            Require(second.SettingsPanel is null && third.SettingsPanel is null,
                "creating instances without settings does not open extra settings windows", checks);

            string greenPath = Path.GetFullPath(Path.Combine(output, "multi-green.png"));
            string bluePath = Path.GetFullPath(Path.Combine(output, "multi-blue.png"));
            MakeSquare(greenPath, Color.FromRgb(110, 199, 150));
            MakeSquare(bluePath, Color.FromRgb(110, 157, 220));
            var originalFrame = primary.CurrentFrame;
            second.SetCharacterForTest(greenPath); third.SetCharacterForTest(bluePath);
            second.Settings.InstanceName = "小绿"; third.Settings.InstanceName = "小蓝";
            second.Settings.Size = third.Settings.Size = 160;
            second.Settings.BubbleFontFamily = "Arial"; second.Settings.BubbleFontSize = 24;
            second.Settings.BubbleScale = 1.5; second.Settings.BubbleFontWeight = 700;
            second.Settings.PresetPhrases = "小绿的话"; second.Settings.CustomPhrases = "小绿的自定义";
            third.Settings.BubbleFontFamily = "Times New Roman"; third.Settings.BubbleFontSize = 10;
            third.Settings.BubbleScale = .5; third.Settings.BubbleFontWeight = 100;
            third.Settings.PresetPhrases = "小蓝的话"; third.Settings.CustomPhrases = "小蓝的自定义";
            second.Settings.Sensitivity = .8; third.Settings.Sensitivity = 2.4;
            second.ApplySettings(); third.ApplySettings();
            Require(primary.Settings.CharacterPath == original.CharacterPath && originalFrame.PixelWidth > 64 &&
                second.Settings.CharacterPath == greenPath && third.Settings.CharacterPath == bluePath &&
                second.CurrentFrame.PixelWidth == 64 && third.CurrentFrame.PixelWidth == 64 && !ReferenceEquals(second.CurrentFrame, third.CurrentFrame),
                "changing each added character leaves the primary GIF and sibling image independent", checks);
            Require(primary.Settings.Size == original.Size && primary.Settings.PresetPhrases == original.PresetPhrases &&
                second.Settings.BubbleFontWeight == 700 && third.Settings.BubbleFontWeight == 100 &&
                second.Settings.Sensitivity != third.Settings.Sensitivity,
                "instance-specific display, dialogue and audio preferences do not alter siblings", checks);
            var sharedMeter = typeof(AudioService).GetField("_sharedMeter", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var capture = typeof(AudioService).GetField("_capture", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Require(coordinator.Instances.All(pet => ReferenceEquals(sharedMeter.GetValue(pet.Audio), coordinator.AudioMeter) && capture.GetValue(pet.Audio) is null),
                "all three windows use one shared system-audio meter without opening private loopback captures", checks);
            Require(!ReferenceEquals(primary.Audio, second.Audio) && !ReferenceEquals(second.Audio, third.Audio),
                "each instance retains its own click-sound playback state", checks);
            await Task.Delay(100);
            SnapshotInstances(coordinator.Instances, Path.Combine(output, "multiple-characters.png"));

            primary.ShowSettings();
            Require(IsSelectorBoundAndVisible(primary, 3),
                "the primary selector renders its character name while retaining the bound window identity", checks);
            SelectInstance(primary, second);
            await Task.Delay(50);
            Require(primary.SettingsPanel is null && second.SettingsPanel?.IsVisible == true && IsSelectorBoundAndVisible(second, 3),
                "selecting another character opens the matching settings panel with a visible selected name", checks);
            double primarySensitivity = primary.Settings.Sensitivity, thirdSensitivity = third.Settings.Sensitivity;
            var secondSensitivity = (Slider)second.SettingsPanel!.FindName("SensitivitySlider");
            secondSensitivity.Value = 1.1;
            Require(second.Settings.Sensitivity == 1.1 && primary.Settings.Sensitivity == primarySensitivity && third.Settings.Sensitivity == thirdSensitivity,
                "editing a panel selected through the combo changes only its bound character", checks);
            secondSensitivity.Value = .8;
            var nameEditor = (TextBox)second.SettingsPanel.FindName("InstanceNameBox");
            nameEditor.Text = "小绿改名";
            Require(second.Settings.InstanceName == "小绿改名" && IsSelectorBoundAndVisible(second, 3) &&
                ((ComboBox)second.SettingsPanel.FindName("InstanceCombo")).Items.OfType<ComboBoxItem>()
                    .All(item => item.Tag is PetWindow pet && Equals(item.Content, pet.Settings.InstanceName)),
                "renaming refreshes visible selector labels without switching their bound character", checks);
            nameEditor.Text = "小绿";

            ((Button)second.SettingsPanel.FindName("CreateInstanceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            uiCreated = coordinator.Instances.Single(pet => !ReferenceEquals(pet, primary) && !ReferenceEquals(pet, second) && !ReferenceEquals(pet, third));
            await Task.Delay(50);
            Require(coordinator.Instances.Count == 4 && second.SettingsPanel is null && IsSelectorBoundAndVisible(uiCreated, 4),
                "creating a character from settings selects and visibly labels the newly bound instance", checks);
            ((Button)uiCreated.SettingsPanel!.FindName("RemoveInstanceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(50);
            Require(coordinator.Instances.Count == 3 && !coordinator.Instances.Contains(uiCreated) &&
                primary.SettingsPanel?.IsVisible == true && IsSelectorBoundAndVisible(primary, 3),
                "deleting the selected character restores a surviving selector and removes the deleted identity", checks);
            SelectInstance(primary, third);
            await Task.Delay(50);
            Require(IsSelectorBoundAndVisible(third, 3), "the selector also switches to the independently named third character", checks);
            SelectInstance(third, primary);
            await Task.Delay(50);
            Require(IsSelectorBoundAndVisible(primary, 3), "switching back restores the primary character's visible name and binding", checks);

            // These blocks are synchronous on the UI thread, so a render tick
            // cannot change an approach velocity between setup and assertions.
            var workArea = Forms.Screen.FromHandle(new WindowInteropHelper(primary).Handle).WorkingArea;
            double x = workArea.Left + workArea.Width * .58, y = workArea.Top + workArea.Height * .58;
            SetBody(primary, workArea.Left + workArea.Width * .2, workArea.Top + workArea.Height * .2, 0, false);
            SetBody(second, x - 40, y, 180, true); SetBody(third, x + 40, y, -180, true);
            coordinator.ResolveCollisions(1d / 60);
            Require(second.Physics.VelocityX < 0 && third.Physics.VelocityX > 0 && !Bounds(second).IntersectsWith(Bounds(third)),
                "visible moving windows rebound and separate on their cached character outlines", checks);
            VerifyNativeCenter(second, checks); VerifyNativeCenter(third, checks);

            SetBody(second, x - 40, y, 300, true); SetBody(third, x + 40, y, 0, false);
            var fixedCenter = new Point(third.Physics.X, third.Physics.Y);
            coordinator.ResolveCollisions(1d / 60);
            Require(second.Physics.VelocityX < 0 && third.Physics.X == fixedCenter.X && third.Physics.Y == fixedCenter.Y && !third.CanMoveForCollision,
                "a moving character rebounds from a fixed sibling without moving the obstacle", checks);

            SetBody(second, x - 40, y, 300, true); SetBody(third, x + 40, y, 90, true);
            var grabbed = typeof(PetWindow).GetField("_grabbed", BindingFlags.Instance | BindingFlags.NonPublic)!;
            grabbed.SetValue(third, true);
            try
            {
                var draggedCenter = new Point(third.Physics.X, third.Physics.Y);
                coordinator.ResolveCollisions(1d / 60);
                Require(!third.CanMoveForCollision && second.Physics.VelocityX < 0 &&
                    third.Physics.X == draggedCenter.X && third.Physics.Y == draggedCenter.Y && third.Physics.VelocityX == 90,
                    "a held character remains immovable during peer contact and cannot inject a stale drag velocity", checks);
                SetBody(second, workArea.Right - 70, y, 300, true);
                SetBody(third, workArea.Right - 10, y, 0, true);
                var outsideCenter = new Point(third.Physics.X, third.Physics.Y);
                coordinator.ResolveCollisions(1d / 60);
                Require(third.Physics.X == outsideCenter.X && third.Physics.Y == outsideCenter.Y,
                    "peer contact does not snap a held character back into a monitor while crossing its edge", checks);
            }
            finally { grabbed.SetValue(third, false); }

            SetBody(second, x - 160, y, 3200, true); SetBody(third, x + 160, y, -3200, true);
            second.Physics.X = x + 160; third.Physics.X = x - 160;
            coordinator.ResolveCollisions(.1);
            Require(second.Physics.X < third.Physics.X && second.Physics.VelocityX < 0 && third.Physics.VelocityX > 0,
                "fast throws cannot tunnel through another visible character between frames", checks);
            VerifyNativeCenter(second, checks); VerifyNativeCenter(third, checks);
            second.Settings.PhysicsEnabled = false; second.ApplySettings();
            Require(second.Physics.VelocityX == 0 && third.Settings.PhysicsEnabled && third.Physics.VelocityX > 0,
                "disabling motion on one instance leaves the other instance moving", checks);
            third.Settings.PhysicsEnabled = false; third.ApplySettings();

            foreach (var pet in coordinator.Instances) pet.CaptureSettingsPosition();
            var saved = coordinator.Instances.Select(pet => pet.Settings.Copy()).ToArray();
            var restored = PetProfiles.Deserialize(PetProfiles.Serialize(saved));
            Require(restored.Count == 3 && restored.Select(p => p.InstanceId).SequenceEqual(saved.Select(p => p.InstanceId)) &&
                restored.Zip(saved).All(pair => pair.First.CenterX == pair.Second.CenterX && pair.First.CenterY == pair.Second.CenterY),
                "the profile document restores all three identities and exact desktop positions", checks);
            Require(restored[1].CharacterPath == greenPath && restored[2].CharacterPath == bluePath &&
                restored[1].BubbleFontFamily == "Arial" && restored[1].BubbleFontSize == 24 && restored[1].BubbleScale == 1.5 && restored[1].BubbleFontWeight == 700 &&
                restored[2].BubbleFontFamily == "Times New Roman" && restored[2].BubbleScale == .5 && restored[2].BubbleFontWeight == 100 &&
                restored[1].PresetPhrases == "小绿的话" && restored[2].CustomPhrases == "小蓝的自定义",
                "restored profiles retain their independent images, bubble styles and dialogue", checks);
            Require(coordinator.RemoveInstance(second) && coordinator.Instances.Count == 2 &&
                ReferenceEquals(coordinator.Instances[0], primary) && primary.IsVisible && third.IsVisible,
                "removing the middle instance leaves the primary and other window running", checks);
            Require(IsSelectorBoundAndVisible(primary, 2), "deleting a sibling refreshes the surviving selector without blanking its name", checks);
            Require(coordinator.RemoveInstance(third) && coordinator.Instances.Count == 1 && !coordinator.RemoveInstance(primary),
                "removing extras keeps one primary window and refuses removal of the last instance", checks);
            Require(IsSelectorBoundAndVisible(primary, 1) && !((Button)primary.SettingsPanel!.FindName("RemoveInstanceButton")).IsEnabled,
                "the last remaining selector stays labeled and disables deletion", checks);
            File.WriteAllText(Path.Combine(output, "multi-pet-result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
            return checks;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "multi-pet-result.json"), JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
        finally
        {
            if (uiCreated is not null && coordinator.Instances.Contains(uiCreated)) coordinator.RemoveInstance(uiCreated);
            if (second is not null && coordinator.Instances.Contains(second)) coordinator.RemoveInstance(second);
            if (third is not null && coordinator.Instances.Contains(third)) coordinator.RemoveInstance(third);
            foreach (var property in typeof(PetSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(property => property.CanWrite))
                property.SetValue(primary.Settings, property.GetValue(original));
            primary.ApplySettings(); primary.Physics.X = primaryPosition.X; primary.Physics.Y = primaryPosition.Y;
            primary.RefreshAfterPeerCollision();
        }
    }

    internal static bool IsSelectorBoundAndVisible(PetWindow pet, int expectedCount)
    {
        if (pet.SettingsPanel is null) return false;
        var selector = (ComboBox)pet.SettingsPanel.FindName("InstanceCombo");
        selector.ApplyTemplate(); pet.SettingsPanel.UpdateLayout();
        return selector.Items.Count == expectedCount && selector.SelectedItem is ComboBoxItem selected &&
            ReferenceEquals(selected.Tag, pet) && Equals(selected.Content, pet.Settings.InstanceName) &&
            selector.SelectionBoxItem is string label && label == pet.Settings.InstanceName &&
            ContainsRenderedName(selector, label);
    }

    private static bool ContainsRenderedName(DependencyObject visual, string expected)
    {
        if (visual is TextBlock text && text.Text == expected && text.IsVisible && text.ActualWidth > 0 && text.ActualHeight > 0) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            if (ContainsRenderedName(VisualTreeHelper.GetChild(visual, i), expected)) return true;
        return false;
    }

    private static void SelectInstance(PetWindow current, PetWindow target)
    {
        var selector = (ComboBox)current.SettingsPanel!.FindName("InstanceCombo");
        selector.SelectedItem = selector.Items.OfType<ComboBoxItem>().Single(item => ReferenceEquals(item.Tag, target));
    }

    private static void SetBody(PetWindow pet, double x, double y, double velocityX, bool moving)
    {
        pet.Settings.PhysicsEnabled = moving; pet.ApplySettings();
        pet.Physics.X = x; pet.Physics.Y = y; pet.Physics.VelocityX = velocityX; pet.Physics.VelocityY = 0;
        pet.Physics.Angle = pet.Physics.AngularVelocity = 0;
        var rotation = (RotateTransform)pet.FindName("Spin"); rotation.Angle = 0;
        var squeeze = (ScaleTransform)pet.FindName("Squash"); squeeze.ScaleX = squeeze.ScaleY = 1;
        ((TranslateTransform)pet.FindName("Bounce")).Y = 0;
        pet.CaptureCollisionStart();
    }
    private static Rect Bounds(PetWindow pet)
    {
        var result = Rect.Empty;
        foreach (var vertex in pet.GetCollisionVertices()) result.Union(vertex);
        return result;
    }
    private static void VerifyNativeCenter(PetWindow pet, List<string> checks)
    {
        Require(GetWindowRect(new WindowInteropHelper(pet).Handle, out var rectangle) &&
            Math.Abs((rectangle.Left + rectangle.Right) / 2d - pet.Physics.X) <= 2 &&
            Math.Abs((rectangle.Top + rectangle.Bottom) / 2d - pet.Physics.Y) <= 2,
            "peer collision updates the actual native window position for " + pet.Settings.InstanceName, checks);
    }
    private static void Require(bool passed, string message, List<string> checks)
    {
        if (!passed) throw new InvalidOperationException(message);
        checks.Add(message);
    }
    private static void MakeSquare(string path, Color color)
    {
        const int side = 64;
        var pixels = new byte[side * side * 4];
        for (int y = 8; y < 56; y++)
        for (int x = 8; x < 56; x++)
        {
            int index = (y * side + x) * 4;
            pixels[index] = color.B; pixels[index + 1] = color.G; pixels[index + 2] = color.R; pixels[index + 3] = 255;
        }
        var frame = BitmapSource.Create(side, side, 96, 96, PixelFormats.Bgra32, null, pixels, side * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static void SnapshotInstances(IReadOnlyList<PetWindow> instances, string path)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(246, 248, 242)), null, new Rect(0, 0, 1020, 380));
            for (int i = 0; i < instances.Count; i++)
            {
                var pet = instances[i]; pet.UpdateLayout();
                drawing.DrawRectangle(new VisualBrush(pet.Content as Visual), null, new Rect(i * 340 + 20, 30, 300, 300));
                var label = new FormattedText(pet.Settings.InstanceName, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei UI"), 16, Brushes.DimGray, 1);
                drawing.DrawText(label, new Point(i * 340 + 24, 340));
            }
        }
        var bitmap = new RenderTargetBitmap(1020, 380, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rectangle);
}
