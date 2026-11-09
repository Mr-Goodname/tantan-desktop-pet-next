using BonbonPet.Core;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
void Near(double actual, double expected, double epsilon, string message) => Check(Math.Abs(actual - expected) <= epsilon, message + $" ({actual} vs {expected})");
PetPhysics NewPet() => new() { X = 500, Y = 400, VelocityX = 180, VelocityY = 130, Angle = 17, AngularVelocity = 45 };

// No-drag energy remains constant through thousands of edge impacts.
var pet = NewPet();
var speed = Math.Sqrt(pet.VelocityX * pet.VelocityX + pet.VelocityY * pet.VelocityY);
for (var i = 0; i < 30000; i++)
{
    pet.Step(1.0 / 60, -1920, -200, 1920, 1080, 120, false);
    Check(pet.X >= -1800 && pet.X <= 1800 && pet.Y >= -80 && pet.Y <= 960, "Pet escaped bounds");
}
Near(Math.Sqrt(pet.VelocityX * pet.VelocityX + pet.VelocityY * pet.VelocityY), speed, 1e-9, "Elastic bounces changed speed");
Near(pet.AngularVelocity, 45, 1e-9, "Free spin changed without drag");

// An outward-moving pet at an edge immediately rebounds; exact hits point inward.
pet = new() { X = 90, Y = 50, VelocityX = 100 };
pet.Step(0.1, 0, 0, 100, 100, 0, false);
Near(pet.X, 100, 1e-9, "Exact boundary hit position");
Check(pet.VelocityX < 0, "Exact boundary hit must point inward");
pet.Step(0.1, 0, 0, 100, 100, 0, false);
Near(pet.X, 90, 1e-9, "Exact boundary subsequent bounce");

// Folding handles multiple crossings in a single step without a per-collision loop.
pet = new() { X = 15, Y = 50, VelocityX = 10000 };
pet.Step(0.1, 0, 0, 100, 100, 10, false);
Near(pet.X, 55, 1e-9, "Multiple crossings final location");
Near(pet.VelocityX, 10000, 1e-9, "Multiple crossings final direction");

// Different timesteps produce the same drag trajectory, including rebounds.
var coarse = NewPet();
var fine = NewPet();
for (var i = 0; i < 100; i++) coarse.Step(0.1, 0, 0, 1000, 800, 90, true);
for (var i = 0; i < 1000; i++) fine.Step(0.01, 0, 0, 1000, 800, 90, true);
Near(coarse.X, fine.X, 1e-7, "Drag trajectory depends on timestep (X)");
Near(coarse.Y, fine.Y, 1e-7, "Drag trajectory depends on timestep (Y)");
Near(coarse.Angle, fine.Angle, 1e-7, "Spin drag depends on timestep");
Near(coarse.VelocityX, fine.VelocityX, 1e-7, "Drag speed depends on timestep");
Check(Math.Abs(coarse.VelocityX) < 180, "Decay did not reduce speed");
for (var i = 0; i < 1000; i++) coarse.Step(0.1, 0, 0, 1000, 800, 90, true);
Check(coarse.VelocityX == 0 && coarse.VelocityY == 0 && coarse.AngularVelocity == 0, "Decay must settle to rest");

// Oversized pets stay centered and stationary rather than oscillating across impossible bounds.
pet = NewPet();
pet.Step(0.02, 0, 0, 100, 100, 120, false);
Near(pet.X, 50, 1e-9, "Oversized pet X");
Near(pet.Y, 50, 1e-9, "Oversized pet Y");
Check(pet.VelocityX == 0 && pet.VelocityY == 0, "Oversized pet velocity");

// Invalid deltas and suspended frames cannot teleport; sanitize accidental nonfinite velocities.
pet = NewPet();
pet.Step(double.NaN, 0, 0, 1000, 800, 0, false);
Near(pet.X, 500, 1e-9, "Invalid time changed position");
pet.Step(200, 0, 0, 1000, 800, 0, false);
Near(pet.X, 518, 1e-9, "Suspended time was not clamped");
pet.VelocityX = double.PositiveInfinity;
pet.Step(0.01, 0, 0, 1000, 800, 0, false);
Check(double.IsFinite(pet.X) && pet.VelocityX == 0, "Nonfinite velocity propagation");

// Kicks span all four quadrants while preserving the chosen speed and a visible spin.
var quadrants = new HashSet<int>();
for (var i = 0; i < 512; i++)
{
    pet.Kick(300);
    Near(Math.Sqrt(pet.VelocityX * pet.VelocityX + pet.VelocityY * pet.VelocityY), 300, 1e-9, "Kick magnitude");
    Check(Math.Abs(pet.AngularVelocity) >= 22 && Math.Abs(pet.AngularVelocity) <= 110, "Kick spin out of range");
    quadrants.Add((pet.VelocityX >= 0 ? 1 : 0) | (pet.VelocityY >= 0 ? 2 : 0));
}
Check(quadrants.Count == 4, "Kicks did not span all directions");
pet.Stop();
Check(pet.VelocityX == 0 && pet.VelocityY == 0 && pet.AngularVelocity == 0, "Stop failed");

// Asymmetric visible edges, rather than a large enclosing circle, govern contact.
pet = new() { X = 900, Y = 400, VelocityX = 40 };
pet.Step(.1, 0, 0, 1000, 800, -95, -120, 50, 70, false);
Near(pet.X, 904, 1e-9, "Visible silhouette bounced before touching");
Check(pet.VelocityX == 40, "Visible silhouette early reflection");
pet.X = 946;
pet.Step(.1, 0, 0, 1000, 800, -95, -120, 50, 70, false);
Near(pet.X + 50, 1000, 1e-9, "Visible edge did not meet screen edge");
Check(pet.VelocityX < 0, "Visible edge did not reflect at contact");
pet = new() { X = 96, Y = 400, VelocityX = -10000 };
pet.Step(.1, 0, 0, 1000, 800, -95, -120, 50, 70, false);
Check(pet.X >= 95 && pet.X <= 950, "Fast mouse throw tunneled through screen edge");
Near(Math.Abs(pet.VelocityX), 10000, 1e-9, "Fast mouse throw lost speed at bounce");
Console.WriteLine($"Physics checks passed: {checks:N0} assertions (bounce, drag, timestep, bounds, kicks).");
