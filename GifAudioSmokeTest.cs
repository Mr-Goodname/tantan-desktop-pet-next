using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BonbonPet.Controls;
using BonbonPet.Core;

namespace BonbonPet;

internal static class GifAudioSmokeTest
{
    internal static Task<List<string>> Run(PetWindow pet, string output)
    {
        var checks = new List<string>();
        var saved = pet.Settings.Copy();
        bool bubbleWasVisible = pet.BubblePanel.IsVisible;
        string previousBubbleText = pet.BubblePanel.CurrentText;
        var fields = typeof(PetWindow).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(field => field.Name is "_sprite" or "_spriteAdvanceAge" or "_animationPlaybackRate" or
                "_audioAverage" or "_beatPulse" or "_beatAge" or "_swayDirection" or "_lastRenderBucket" or
                "_lastTime" or "_previewReleaseAt" or "_bubbleDeadline" or "_bubbleUntil")
            .ToDictionary(field => field, field => field.GetValue(pet));
        var spring = (PressSpring)Field("_pressSpring").GetValue(pet)!;
        var springFields = typeof(PressSpring).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .ToDictionary(field => field, field => field.GetValue(spring));
        var position = new Point(pet.Physics.X, pet.Physics.Y);
        double angle = pet.Physics.Angle, vx = pet.Physics.VelocityX, vy = pet.Physics.VelocityY,
            angularVelocity = pet.Physics.AngularVelocity;
        var squash = (ScaleTransform)pet.FindName("Squash");
        var bounce = (TranslateTransform)pet.FindName("Bounce");
        var spin = (RotateTransform)pet.FindName("Spin");
        double originalX = squash.ScaleX, originalY = squash.ScaleY, originalBounce = bounce.Y, originalSpin = spin.Angle;
        double stamp = 100000;
        double quietClock = 0, loudClock = 0;
        try
        {
            pet.Settings.PhysicsEnabled = false; pet.Settings.AudioEnabled = false;
            pet.Settings.BubblesEnabled = false; pet.Settings.SqueezeStrength = 0; pet.Settings.SqueakVolume = 0;
            pet.Settings.HideOnFullscreen = false; pet.Settings.Fps = 60; pet.Settings.Sensitivity = 1.3;
            pet.Settings.GifAudioMode = "speed";
            pet.ApplySettings(); pet.BubblePanel.Hide();
            ResetGif(pet);
            using (var normal = SpriteAnimation.LoadDefault())
            {
                for (int i = 0; i < 100; i++)
                {
                    Render(1, .02); normal.Advance(.02);
                    if (!ReferenceEquals(normal.CurrentFrame, pet.CurrentFrame))
                        throw new InvalidOperationException("Audio-disabled GIF changed its native frame timing at step " + i);
                }
            }
            Require(pet.CurrentAnimationPlaybackRate == 1, "disabling audio keeps GIF playback at its native frame timing", checks);

            pet.Settings.AudioEnabled = true;
            ResetGif(pet);
            using (var expected = SpriteAnimation.LoadDefault())
            {
                for (int i = 0; i < 100; i++)
                {
                    Render(0, .02);
                    quietClock += .02 * pet.CurrentAnimationPlaybackRate;
                    expected.Advance(.02 * pet.CurrentAnimationPlaybackRate);
                    if (!ReferenceEquals(expected.CurrentFrame, pet.CurrentFrame))
                        throw new InvalidOperationException("Quiet GIF frame timeline mismatch at step " + i);
                }
            }
            Require(pet.CurrentAnimationPlaybackRate >= .65 && pet.CurrentAnimationPlaybackRate < .66,
                "a quiet audio-enabled GIF eases down to slower native animation", checks);
            Require(Math.Abs(squash.ScaleX - 1) < .001 && Math.Abs(squash.ScaleY - 1) < .001 && bounce.Y == 0 && spin.Angle == 0,
                "GIF audio adds no scale deformation, bouncing or nonphysical rotation", checks);

            ResetGif(pet);
            bool smooth = true, bounded = true, framesMatch = true;
            double previousRate = pet.CurrentAnimationPlaybackRate;
            using (var expected = SpriteAnimation.LoadDefault())
            {
                for (int i = 0; i < 100; i++)
                {
                    Render(i % 10 < 5 ? 1 : 0, .02);
                    double rate = pet.CurrentAnimationPlaybackRate;
                    smooth &= Math.Abs(rate - previousRate) < .45;
                    bounded &= double.IsFinite(rate) && rate >= .65 && rate <= 2.6;
                    previousRate = rate; loudClock += .02 * rate; expected.Advance(.02 * rate);
                    framesMatch &= ReferenceEquals(expected.CurrentFrame, pet.CurrentFrame);
                }
            }
            Require(loudClock > quietClock * 1.4, "music accents advance the GIF's own animation faster than quiet audio", checks);
            Require(smooth && bounded, "GIF animation speed changes smoothly inside the 0.65–2.6 range", checks);
            Require(framesMatch, "GIF frames advance by accumulated audio-scaled frame durations", checks);
            Require(Math.Abs(squash.ScaleX - 1) < .001 && Math.Abs(squash.ScaleY - 1) < .001 && bounce.Y == 0 && spin.Angle == 0,
                "loud GIF animation uses its original artwork without layered audio deformation", checks);
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1d, 4d }) Render(invalid, .02);
            Require(double.IsFinite(pet.CurrentAnimationPlaybackRate) && pet.CurrentAnimationPlaybackRate is >= .65 and <= 2.6,
                "nonfinite or out-of-range test audio cannot corrupt the animation clock", checks);

            pet.Settings.SqueezeStrength = 1.8;
            spring.Press(pet.Settings.SqueezeStrength); Render(1, .02);
            Require(squash.ScaleX > 1.12 && squash.ScaleY < .82 && bounce.Y == 0,
                "GIF native animation retains separate mouse-press compression", checks);
            spring.Release(); pet.Settings.SqueezeStrength = 0; Render(0, .02);
            pet.Settings.PhysicsEnabled = true; pet.ApplySettings();
            pet.Physics.Angle = 37; pet.Physics.AngularVelocity = pet.Physics.VelocityX = pet.Physics.VelocityY = 0;
            Render(1, .02);
            Require(Math.Abs(spin.Angle - 37) < .001, "GIF audio leaves physical rotation controlled by the physics angle", checks);
            pet.Settings.PhysicsEnabled = false; pet.ApplySettings();

            pet.SettingsPanel!.Reload();
            var modeSelector = (ComboBox)pet.SettingsPanel.FindName("GifAudioModeCombo");
            Require(modeSelector.IsEnabled && pet.IsAnimatedCharacter,
                "the animated character exposes its GIF audio response selector", checks);
            modeSelector.SelectedItem = modeSelector.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == "deform");
            Require(pet.Settings.GifAudioMode == "deform", "selecting GIF deformation in settings updates the bound character mode", checks);
            ResetGif(pet);
            bool originalTiming = true;
            using (var nativeTiming = SpriteAnimation.LoadDefault())
            {
                for (int i = 0; i < 80; i++)
                {
                    Render(i % 10 < 5 ? 1 : 0, .02); nativeTiming.Advance(.02);
                    originalTiming &= pet.CurrentAnimationPlaybackRate == 1 && ReferenceEquals(nativeTiming.CurrentFrame, pet.CurrentFrame);
                }
                Render(1, .02); nativeTiming.Advance(.02);
                Require(pet.IsAnimatedCharacter && originalTiming && ReferenceEquals(nativeTiming.CurrentFrame, pet.CurrentFrame),
                    "GIF deformation mode keeps native animation frame timing", checks);
                Require(squash.ScaleX < .98 && squash.ScaleY > 1.08 && bounce.Y < -1 && Math.Abs(spin.Angle) > .5,
                    "GIF deformation mode restores audio stretch, bounce and nonphysical sway", checks);
                modeSelector.SelectedItem = modeSelector.Items.OfType<ComboBoxItem>().Single(item => (string)item.Tag == "speed");
                Require(pet.Settings.GifAudioMode == "speed", "selecting GIF animation speed in settings updates the bound character mode", checks);
                Render(1, .02);
                nativeTiming.Advance(.02 * pet.CurrentAnimationPlaybackRate);
                Require(pet.CurrentAnimationPlaybackRate > 1 && ReferenceEquals(nativeTiming.CurrentFrame, pet.CurrentFrame) &&
                    Math.Abs(squash.ScaleX - 1) < .001 && Math.Abs(squash.ScaleY - 1) < .001 && bounce.Y == 0 && spin.Angle == 0,
                    "switching GIF mode resumes audio-scaled timing without resetting frames or retaining deformation", checks);
            }
            pet.Settings.GifAudioMode = "deform"; pet.Settings.AudioEnabled = false; Render(1, .02);
            Require(pet.CurrentAnimationPlaybackRate == 1 && Math.Abs(squash.ScaleX - 1) < .001 &&
                Math.Abs(squash.ScaleY - 1) < .001 && bounce.Y == 0 && spin.Angle == 0,
                "disabling GIF audio also restores native timing and pose in deformation mode", checks);
            pet.Settings.AudioEnabled = true; pet.Settings.GifAudioMode = "speed";

            string staticPath = Path.GetFullPath(Path.Combine(output, "gif-audio-static.png"));
            MakeStatic(staticPath); pet.SetCharacterForTest(staticPath); ResetClock(pet);
            pet.SettingsPanel.Reload();
            Require(!modeSelector.IsEnabled, "importing a static image disables the GIF-specific response selector", checks);
            var stillFrame = pet.CurrentFrame;
            Render(1, .02);
            Require(!pet.IsAnimatedCharacter && pet.CurrentAnimationPlaybackRate == 1 && ReferenceEquals(pet.CurrentFrame, stillFrame),
                "static artwork stays on its single original frame", checks);
            Require(squash.ScaleX < .98 && squash.ScaleY > 1.08 && bounce.Y < -1 && Math.Abs(spin.Angle) > .5,
                "static artwork retains audio stretch, bounce and nonphysical sway", checks);
            pet.Settings.AudioEnabled = false; Render(1, .02);
            Require(Math.Abs(squash.ScaleX - 1) < .001 && Math.Abs(squash.ScaleY - 1) < .001 && bounce.Y == 0 && spin.Angle == 0,
                "disabling static audio restores the unchanged image pose", checks);
            var normalized = new PetSettings { GifAudioMode = "unknown" };
            normalized.Normalize();
            Require(normalized.GifAudioMode == "speed", "unknown saved GIF modes recover to animation speed", checks);
            normalized.GifAudioMode = "deform"; normalized.Normalize();
            Require(normalized.GifAudioMode == "deform", "saved GIF deformation mode remains available after normalization", checks);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "gif-audio-result.json"), JsonSerializer.Serialize(
                new { passed = true, checks, quietAnimationSeconds = quietClock, loudAnimationSeconds = loudClock },
                new JsonSerializerOptions { WriteIndented = true }));
            return Task.FromResult(checks);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "gif-audio-result.json"), JsonSerializer.Serialize(
                new { passed = false, checks, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
        finally
        {
            foreach (var property in typeof(PetSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.CanWrite))
                property.SetValue(pet.Settings, property.GetValue(saved));
            foreach (var entry in fields) entry.Key.SetValue(pet, entry.Value);
            foreach (var entry in springFields) entry.Key.SetValue(spring, entry.Value);
            typeof(PetWindow).GetMethod("BuildOutlines", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(pet, null);
            ((Image)pet.FindName("PetImage")).Source = pet.CurrentFrame;
            pet.ApplySettings(); pet.Physics.X = position.X; pet.Physics.Y = position.Y;
            pet.Physics.Angle = angle; pet.Physics.VelocityX = vx; pet.Physics.VelocityY = vy; pet.Physics.AngularVelocity = angularVelocity;
            squash.ScaleX = originalX; squash.ScaleY = originalY; bounce.Y = originalBounce; spin.Angle = originalSpin;
            pet.RefreshAfterPeerCollision(); pet.SettingsPanel?.Reload();
            if (bubbleWasVisible) pet.BubblePanel.Say(previousBubbleText); else pet.BubblePanel.Hide();
            // ApplySettings intentionally touches these timers; preserve their
            // exact pre-test state after it has restored the actual controls.
            foreach (var entry in fields.Where(entry => entry.Key.Name != "_sprite")) entry.Key.SetValue(pet, entry.Value);
        }

        void Render(double level, double dt)
        {
            stamp += Math.Max(dt, .02);
            pet.RenderAudioForTest(level, dt, TimeSpan.FromSeconds(stamp));
        }
    }

    private static FieldInfo Field(string name) => typeof(PetWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static void ResetGif(PetWindow pet) { pet.ResetCharacter(); ResetClock(pet); }
    private static void ResetClock(PetWindow pet)
    {
        foreach (string name in new[] { "_spriteAdvanceAge", "_audioAverage", "_beatPulse" }) Field(name).SetValue(pet, 0d);
        foreach (string name in new[] { "_animationPlaybackRate", "_beatAge", "_swayDirection" }) Field(name).SetValue(pet, 1d);
        Field("_lastRenderBucket").SetValue(pet, -1L);
    }
    private static void Require(bool pass, string message, List<string> checks)
    {
        if (!pass) throw new InvalidOperationException(message);
        checks.Add(message);
    }
    private static void MakeStatic(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pixels = new byte[64 * 64 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        { pixels[i] = 120; pixels[i + 1] = 200; pixels[i + 2] = 150; pixels[i + 3] = 255; }
        var frame = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
