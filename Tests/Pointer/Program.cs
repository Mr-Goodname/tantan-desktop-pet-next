using BonbonPet.Core;

int checks = 0;
void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }
void Near(double value, double expected, double tolerance, string message) => Check(Math.Abs(value - expected) <= tolerance, $"{message}: {value:F3} vs {expected:F3}");
var pointer = new PointerVelocity();

// Constant movement must preserve real mouse speed at different event rates,
// including speeds far above the old 650 px/sec cap.
foreach (int frequency in new[] { 10, 20, 30, 60, 125, 500, 1000 })
{
    pointer.Reset(10000, -1234, 550);
    for (int i = 1; i <= frequency; i++)
        pointer.Add(10000 + (double)i / frequency, -1234 + 3600.0 * i / frequency, 550 - 1400.0 * i / frequency);
    var v = pointer.Estimate(10001);
    Near(v.X, 3600, .001, $"Constant X speed at {frequency} Hz");
    Near(v.Y, -1400, .001, $"Constant Y speed at {frequency} Hz");
}

// A slow initial drag followed by a fast final stroke should use the final
// stroke, not the average of the whole drag or half a one-frame delta.
pointer.Reset(0, 0, 0);
for (int i = 1; i <= 20; i++) pointer.Add(i * .01, i * 2, 0);
for (int i = 1; i <= 10; i++) pointer.Add(.2 + i * .01, 40 + i * 35, 0);
Near(pointer.Estimate(.3).X, 3500, .001, "Recent fast stroke retained");

// Starting faster near the end should not be dominated by older slow samples.
pointer.Reset(0, 0, 0);
for (int i = 1; i <= 50; i++)
{
    double t = i * .01;
    pointer.Add(t, 10000 * t * t, 0);
}
var accelerated = pointer.Estimate(.5);
Check(accelerated.X > 8800 && accelerated.X < 10000, $"Accelerating stroke follows recent motion ({accelerated.X:F1})");

// A hold after a flick must not launch the pet using the earlier movement.
pointer.Reset(0, 0, 0); pointer.Add(.02, 100, 0); pointer.Add(.2, 100, 0);
Near(pointer.Estimate(.2).X, 0, 0, "Hold after flick releases at rest");
pointer.Reset(0, 0, 0); pointer.Add(.02, 100, 0);
Near(pointer.Estimate(.2).X, 0, 0, "Sparse held pointer releases at rest");

// MouseUp can carry a final position that did not receive MouseMove. Include
// that actual release sample; it should complete the trajectory accurately.
pointer.Reset(0, 0, 0); pointer.Add(.02, 80, -40); pointer.Add(.04, 160, -80); pointer.Add(.06, 240, -120);
var finalPoint = pointer.Estimate(.06);
Near(finalPoint.X, 4000, .001, "Final release position included");
Near(finalPoint.Y, -2000, .001, "Final release Y included");

// Duplicate/out-of-order/nonfinite timestamps cannot produce NaN/Infinity.
pointer.Reset(1, 0, 0); pointer.Add(1, 1, 1); pointer.Add(.9, 500, 500); pointer.Add(double.NaN, 5, 5);
var invalid = pointer.Estimate(1);
Near(invalid.X, 0, 0, "Zero-duration samples do not launch");
pointer.Add(1.02, 81, 41);
var duplicate = pointer.Estimate(1.02);
Near(duplicate.X, 4000, .001, "Duplicate timestamp keeps last coordinate");
Near(duplicate.Y, 2000, .001, "Duplicate timestamp Y");
Near(pointer.Estimate(double.NaN).X, 0, 0, "Invalid release is at rest");
Near(pointer.Estimate(1.01).X, 0, 0, "Release predating samples is at rest");
Console.WriteLine($"Pointer velocity checks passed: {checks} (real speed, rates, final flick, holds, invalid events).");
