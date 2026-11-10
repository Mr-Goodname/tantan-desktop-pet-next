using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BonbonPet.Core;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 1e-7, $"{message}: {actual} versus {expected}");
double Energy(PetPhysics a, PetPhysics b, double massA = 1, double massB = 1) =>
    .5 * massA * (a.VelocityX * a.VelocityX + a.VelocityY * a.VelocityY) +
    .5 * massB * (b.VelocityX * b.VelocityX + b.VelocityY * b.VelocityY);
Point[] Box(PetPhysics body, double halfWidth = 10, double halfHeight = 10, double angle = 0)
{
    var vertices = new Point[4]; BoxInto(vertices, body, halfWidth, halfHeight, angle); return vertices;
}
void BoxInto(Span<Point> vertices, PetPhysics body, double halfWidth = 10, double halfHeight = 10, double angle = 0)
{
    var (sin, cos) = Math.SinCos(angle * Math.PI / 180);
    for (int i = 0; i < 4; i++)
    {
        double x = i is 0 or 3 ? -halfWidth : halfWidth, y = i < 2 ? -halfHeight : halfHeight;
        vertices[i] = new Point(body.X + x * cos - y * sin, body.Y + x * sin + y * cos);
    }
}
BitmapSource Make(int width, int height, Func<int, int, byte> alpha, double dpiX = 96, double dpiY = 96)
{
    var pixels = new byte[width * height * 4];
    for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++) pixels[(y * width + x) * 4 + 3] = alpha(x, y);
    var frame = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Bgra32, null, pixels, width * 4);
    frame.Freeze(); return frame;
}
bool Overlaps(PetPhysics a, PetPhysics b, double halfA, double halfB) =>
    Math.Abs(a.X - b.X) < halfA + halfB && Math.Abs(a.Y - b.Y) < halfA + halfB;

var a = new PetPhysics { X = 0, VelocityX = 100, VelocityY = 12 };
var b = new PetPhysics { X = 18, VelocityX = -100, VelocityY = -20 };
double initialEnergy = Energy(a, b);
Check(PeerCollision.Resolve(a, Box(a), true, b, Box(b), true), "Head-on collision was missed");
Near(a.VelocityX, -90, "First body rebounds"); Near(b.VelocityX, 90, "Second body rebounds");
Near(a.VelocityY, 12, "First tangential velocity preserved"); Near(b.VelocityY, -20, "Second tangential velocity preserved");
Near(a.VelocityX + b.VelocityX, 0, "Equal-mass momentum preserved");
Check(Energy(a, b) < initialEnergy, "Impact creates energy");
Check(!Overlaps(a, b, 10, 10), "Positional correction leaves an overlap");
Check(!PeerCollision.Resolve(a, Box(a), true, b, Box(b), true), "Separated bodies bounce repeatedly");

a = new PetPhysics { VelocityX = 200 }; b = new PetPhysics { X = 18 };
initialEnergy = Energy(a, b, 1, 3);
Check(PeerCollision.Resolve(a, Box(a), true, b, Box(b), true, 1, 3), "Unequal-mass contact missed");
Near(a.VelocityX, -85, "Light body velocity"); Near(b.VelocityX, 95, "Heavy body velocity");
Near(a.VelocityX + 3 * b.VelocityX, 200, "Unequal-mass momentum preserved");
Near(Math.Abs(a.X) / (b.X - 18), 3, "Correction respects inverse mass");
Check(Energy(a, b, 1, 3) <= initialEnergy, "Unequal-mass impact creates energy");

a = new PetPhysics { VelocityX = 100 }; b = new PetPhysics { X = 18, VelocityX = -999, VelocityY = 555 };
Check(PeerCollision.Resolve(a, Box(a), true, b, Box(b), false), "Immovable body contact missed");
Near(a.VelocityX, -90, "Immovable body does not inject its stale velocity");
Near(b.X, 18, "Fixed body X unchanged"); Near(b.Y, 0, "Fixed body Y unchanged");
Near(b.VelocityX, -999, "Fixed body stored velocity unchanged");
Check(!Overlaps(a, b, 10, 10), "Moving body not separated from fixed body");

a = new PetPhysics { VelocityX = -100 }; b = new PetPhysics { X = 18, VelocityX = 100 };
Check(PeerCollision.Resolve(a, Box(a), true, b, Box(b), true), "Separating overlap should still be corrected");
Near(a.VelocityX, -100, "Separating first velocity unchanged"); Near(b.VelocityX, 100, "Separating second velocity unchanged");
a = new PetPhysics(); b = new PetPhysics { X = 18 };
Check(PeerCollision.Resolve(a, Box(a), false, b, Box(b), false), "Two fixed bodies still report contact");
Near(a.X, 0, "First fixed center unchanged"); Near(b.X, 18, "Second fixed center unchanged");

a = new PetPhysics(); b = new PetPhysics();
Check(PeerCollision.Resolve(a, Box(a, 20, 20), true, b, Box(b, 5, 5), true), "Contained body contact missed");
Check(!Overlaps(a, b, 20, 5), "Containment correction uses intersected interval instead of full exit distance");
Near(Energy(a, b), 0, "Correcting a stationary overlap injects velocity");

a = new PetPhysics(); b = new PetPhysics { Y = 6 };
Check(!PeerCollision.Resolve(a, Box(a, 20, 1, 45), true, b, Box(b, 20, 1, 45), true), "Overlapping AABBs falsely collide for separated rotated outlines");
b.Y = 1; a.VelocityY = 100; b.VelocityY = -100;
initialEnergy = Energy(a, b);
Check(PeerCollision.Resolve(a, Box(a, 20, 1, 45), true, b, Box(b, 20, 1, 45), true), "Rotated thin outlines contact missed");
Check(Energy(a, b) <= initialEnergy + 1e-7, "Rotated collision creates energy");

// Pixel hulls exclude the large transparent margins of the actual sprite.
var tinySprite = new SpriteOutline(Make(100, 100, (x, y) => x is >= 49 and < 51 && y is >= 49 and < 51 ? (byte)255 : (byte)0));
var verticesA = new Point[tinySprite.VertexCount]; var verticesB = new Point[tinySprite.VertexCount];
a = new PetPhysics(); b = new PetPhysics { X = 10 };
tinySprite.CopyVertices(verticesA, 100, 100, 1, 1, 0, 0, 1, a.X, a.Y);
tinySprite.CopyVertices(verticesB, 100, 100, 1, 1, 0, 0, 1, b.X, b.Y);
Check(!PeerCollision.Resolve(a, verticesA, true, b, verticesB, true), "Transparent canvas is used as a collision body");
var transparent = new SpriteOutline(Make(8, 8, (_, _) => 0));
Check(transparent.CopyVertices(Span<Point>.Empty, 100, 100, 1, 1, 0, 0, 1, 0, 0) == 0, "Transparent frame has peer geometry");
Check(!PeerCollision.Resolve(a, ReadOnlySpan<Point>.Empty, true, b, verticesB, true), "Empty hull causes a contact");

// Cross-check all transformed hull extrema against the existing WPF-verified
// silhouette bounds, including unequal source DPI, press scale, rotation and bob.
var outline = new SpriteOutline(Make(100, 80, (x, y) => x is >= 10 and < 60 && y is >= 20 and < 50 ? (byte)255 : (byte)0, 192, 96));
var hull = new Point[outline.VertexCount];
foreach (double angle in new[] { 0d, 37, 90, 251 })
{
    const double worldScale = 1.5, centerX = 440, centerY = 230;
    var bounds = outline.GetBounds(220, 220, 1.34, .6, angle, -9);
    Check(outline.CopyVertices(hull, 220, 220, 1.34, .6, angle, -9, worldScale, centerX, centerY) == outline.VertexCount, "Hull count changed during transform");
    Near(hull.Min(p => p.X), centerX + bounds.Left * worldScale, "Transformed hull left");
    Near(hull.Max(p => p.X), centerX + bounds.Right * worldScale, "Transformed hull right");
    Near(hull.Min(p => p.Y), centerY + bounds.Top * worldScale, "Transformed hull top");
    Near(hull.Max(p => p.Y), centerY + bounds.Bottom * worldScale, "Transformed hull bottom");
}
Check(outline.CopyVertices(hull, 0, 220, 1, 1, 0, 0, 1, 0, 0) == 0, "Unavailable layout yields geometry");
var compressedOutline = new SpriteOutline(Make(20, 20, (_, _) => 255));
a = new PetPhysics { VelocityX = 100 }; b = new PetPhysics { X = 120, VelocityX = -100 };
compressedOutline.CopyVertices(verticesA, 100, 100, 1.34, .6, 0, -9, 1, a.X, a.Y);
compressedOutline.CopyVertices(verticesB, 100, 100, 1.34, .6, 0, -9, 1, b.X, b.Y);
Check(PeerCollision.Resolve(a, verticesA, true, b, verticesB, true), "Pressed sprite contacts ignore its current deformation");
Check(a.VelocityX < 0 && b.VelocityX > 0, "Pressed sprites fail to rebound");

// A fast throw crosses another sprite entirely between rendered frames.
a = new PetPhysics { X = 100, VelocityX = 20000 }; b = new PetPhysics { X = -100, VelocityX = -20000 };
initialEnergy = Energy(a, b);
Check(PeerCollision.ResolveSwept(a, Box(a), true, new Point(-100, 0), b, Box(b), true, new Point(100, 0), .01), "Fast head-on bodies tunnel through one another");
Check(a.X < b.X && a.VelocityX < 0 && b.VelocityX > 0, "Fast contact does not retain the pre-impact ordering");
Check(Energy(a, b) <= initialEnergy, "Swept collision creates energy");
Check(!Overlaps(a, b, 10, 10), "Swept contact leaves a final overlap");
a = new PetPhysics { X = 100, VelocityX = 20000 }; b = new PetPhysics();
Check(PeerCollision.ResolveSwept(a, Box(a), true, new Point(-100, 0), b, Box(b), false, new Point(0, 0), .01), "Fast throw tunnels through a fixed body");
Check(a.X < -20 && a.VelocityX < 0, "Fixed-body swept reflection failed"); Near(b.X, 0, "Swept fixed obstacle moved");

// Contact at the start of a step is distinct from an already-penetrating pair.
a = new PetPhysics { X = 180, VelocityX = 20000 }; b = new PetPhysics();
Check(PeerCollision.ResolveSwept(a, Box(a), true, new Point(-20, 0), b, Box(b), false, new Point(0, 0), .01), "A tangent-start fast throw tunnels through a fixed body");
Check(a.X < -20 && a.VelocityX < 0, "Tangent-start hit should occur at time zero");
a = new PetPhysics { X = -20, VelocityX = -100 }; b = new PetPhysics { X = 38, VelocityX = 100 };
Check(!PeerCollision.ResolveSwept(a, Box(a), true, new Point(0, 0), b, Box(b), true, new Point(18, 0), .1), "Already-separating bodies replay a past contact");
a = new PetPhysics { X = 100, Y = 25, VelocityX = 20000 }; b = new PetPhysics();
Check(!PeerCollision.ResolveSwept(a, Box(a), true, new Point(-100, 25), b, Box(b), false, new Point(0, 0), .01), "A nearby fast miss causes a false swept bounce");

// Random unequal masses exercise the impulse invariants independently of a
// particular expected velocity formula.
var random = new Random(1110);
for (int i = 0; i < 300; i++)
{
    double massA = .5 + random.NextDouble() * 20, massB = .5 + random.NextDouble() * 20;
    a = new PetPhysics { VelocityX = random.NextDouble() * 500, VelocityY = random.NextDouble() * 300 - 150 };
    b = new PetPhysics { X = 18, VelocityX = -random.NextDouble() * 500, VelocityY = random.NextDouble() * 300 - 150 };
    double momentumX = massA * a.VelocityX + massB * b.VelocityX, momentumY = massA * a.VelocityY + massB * b.VelocityY;
    initialEnergy = Energy(a, b, massA, massB);
    Check(PeerCollision.Resolve(a, Box(a), true, b, Box(b), true, massA, massB), "Random contact missed");
    Near(massA * a.VelocityX + massB * b.VelocityX, momentumX, "Random X momentum");
    Near(massA * a.VelocityY + massB * b.VelocityY, momentumY, "Random Y momentum");
    Check(Energy(a, b, massA, massB) <= initialEnergy + 1e-7, "Random contact creates energy");
}

// Reuse both sprite and contact buffers in the hot path.
a = new PetPhysics(); b = new PetPhysics(); verticesA = new Point[4]; verticesB = new Point[4];
for (int i = 0; i < 10; i++)
{
    a.X = 0; b.X = 18; a.VelocityX = 100; b.VelocityX = -100;
    BoxInto(verticesA, a); BoxInto(verticesB, b); PeerCollision.Resolve(a, verticesA, true, b, verticesB, true);
    outline.CopyVertices(hull, 220, 220, 1.34, .6, 37, -9, 1.5, 440, 230);
}
long before = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 10000; i++)
{
    a.X = 0; b.X = 18; a.VelocityX = 100; b.VelocityX = -100;
    BoxInto(verticesA, a); BoxInto(verticesB, b); PeerCollision.Resolve(a, verticesA, true, b, verticesB, true);
    outline.CopyVertices(hull, 220, 220, 1.34, .6, i % 360, -9, 1.5, 440, 230);
    a.X = 100; b.X = -100; a.VelocityX = 20000; b.VelocityX = -20000;
    BoxInto(verticesA, a); BoxInto(verticesB, b);
    PeerCollision.ResolveSwept(a, verticesA, true, new Point(-100, 0), b, verticesB, true, new Point(100, 0), .01);
}
Check(GC.GetAllocatedBytesForCurrentThread() == before, "Peer contact and transformed hull hot paths allocate");
Console.WriteLine($"{checks} peer collision checks passed; 10,000 contacts, sweeps and hull transforms allocated zero bytes.");
