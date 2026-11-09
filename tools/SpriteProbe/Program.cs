using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BonbonPet.Controls;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}
using var pet = SpriteAnimation.LoadDefault();
Check(pet.Frames.Count == 27 && pet.IsAnimated, "default GIF has 27 animated frames");
Check(pet.CurrentFrame.PixelWidth == 357 && pet.CurrentFrame.PixelHeight == 384, "bounded default size");
byte[] pixels = new byte[357 * 384 * 4];
pet.CurrentFrame.CopyPixels(pixels, 357 * 4, 0);
Check(pixels[3] == 0, "default edge alpha preserved");
var first = pet.CurrentFrame;
pet.Advance(.101);
Check(!ReferenceEquals(first, pet.CurrentFrame), "advances at GIF frame delay");
pet.Advance(2.6);
Check(ReferenceEquals(first, pet.CurrentFrame), "loops without duration drift");
using var custom = SpriteAnimation.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "disposal.gif"));
Check(custom.Frames.Count == 4, "custom GIF four frames");
for (int i = 0; i < 4; i++)
{
    using var expectedStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", $"expected-{i}.png"));
    var expected = BitmapDecoder.Create(expectedStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
    var formatted = new FormatConvertedBitmap(expected, PixelFormats.Pbgra32, null, 0);
    var want = new byte[40 * 30 * 4];
    var got = new byte[want.Length];
    formatted.CopyPixels(want, 40 * 4, 0);
    custom.Frames[i].CopyPixels(got, 40 * 4, 0);
    Check(want.AsSpan().SequenceEqual(got), $"GIF offsets/transparency/disposal frame {i}");
}
try { SpriteAnimation.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "unsupported.svg")); throw new Exception("Invalid type accepted"); }
catch (InvalidDataException) { Check(true, "unsupported type reports clear error"); }
Console.WriteLine($"{passed} sprite checks passed.");
