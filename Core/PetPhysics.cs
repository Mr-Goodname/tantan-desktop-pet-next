using System;

namespace BonbonPet.Core;

/// <summary>A constant-cost, gravity-free DVD-style bounce simulation. Coordinates share the caller's unit.</summary>
public sealed class PetPhysics
{
    private const double DragPerSecond = 0.26;
    public double X { get; set; }
    public double Y { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public double Angle { get; set; }
    public double AngularVelocity { get; set; }

    /// <param name="radius">A conservative circle enclosing the rotating pet, including its current squash.</param>
    /// <param name="decay">Applies continuous exponential drag; otherwise rebounds preserve speed.</param>
    public void Step(double dt, double left, double top, double right, double bottom, double radius, bool decay)
    {
        radius = double.IsFinite(radius) ? Math.Max(0, radius) : 0;
        Step(dt, left, top, right, bottom, -radius, -radius, radius, radius, decay);
    }

    public double AngleAfter(double dt, bool decay)
    {
        dt = double.IsFinite(dt) ? Math.Clamp(dt, 0, .1) : 0;
        double travelTime = decay ? (1 - Math.Exp(-DragPerSecond * dt)) / DragPerSecond : dt;
        return PositiveModulo(FiniteOrZero(Angle) + FiniteOrZero(AngularVelocity) * travelTime, 360);
    }

    /// <summary>Offsets are the current transformed visible silhouette, relative to its center.</summary>
    public void Step(double dt, double left, double top, double right, double bottom,
        double offsetLeft, double offsetTop, double offsetRight, double offsetBottom, bool decay)
    {
        if (!double.IsFinite(dt) || dt <= 0 || !double.IsFinite(left) || !double.IsFinite(top) ||
            !double.IsFinite(right) || !double.IsFinite(bottom) || right <= left || bottom <= top)
            return;

        // Suspend/resume must not teleport the pet through a long interval.
        dt = Math.Min(dt, 0.1);
        if (!double.IsFinite(offsetLeft) || !double.IsFinite(offsetTop) ||
            !double.IsFinite(offsetRight) || !double.IsFinite(offsetBottom)) return;
        VelocityX = FiniteOrZero(VelocityX);
        VelocityY = FiniteOrZero(VelocityY);
        AngularVelocity = FiniteOrZero(AngularVelocity);
        X = double.IsFinite(X) ? X : (left + right) / 2;
        Y = double.IsFinite(Y) ? Y : (top + bottom) / 2;
        Angle = FiniteOrZero(Angle);

        var damping = decay ? Math.Exp(-DragPerSecond * dt) : 1;
        var travelTime = decay ? (1 - damping) / DragPerSecond : dt;
        var x = X;
        var y = Y;
        var vx = VelocityX;
        var vy = VelocityY;
        AdvanceAxis(ref x, ref vx, left - offsetLeft, right - offsetRight, travelTime);
        AdvanceAxis(ref y, ref vy, top - offsetTop, bottom - offsetBottom, travelTime);
        X = x;
        Y = y;
        VelocityX = vx * damping;
        VelocityY = vy * damping;
        Angle = PositiveModulo(Angle + AngularVelocity * travelTime, 360);
        AngularVelocity *= damping;

        if (decay)
        {
            if (Math.Abs(VelocityX) < 0.25) VelocityX = 0;
            if (Math.Abs(VelocityY) < 0.25) VelocityY = 0;
            if (Math.Abs(AngularVelocity) < 0.1) AngularVelocity = 0;
        }
    }

    /// <summary>Sets a new random velocity of the requested magnitude and gives the pet a gentle free spin.</summary>
    public void Kick(double speed)
    {
        speed = double.IsFinite(speed) ? Math.Clamp(Math.Abs(speed), 0, 5000) : 0;
        var direction = Random.Shared.NextDouble() * Math.Tau;
        VelocityX = Math.Cos(direction) * speed;
        VelocityY = Math.Sin(direction) * speed;
        if (speed == 0)
        {
            AngularVelocity = 0;
            return;
        }
        var spin = 22 + Random.Shared.NextDouble() * 88;
        AngularVelocity = Random.Shared.Next(2) == 0 ? spin : -spin;
    }

    public void Stop()
    {
        VelocityX = VelocityY = AngularVelocity = 0;
    }

    private static void AdvanceAxis(ref double position, ref double velocity, double minimum, double maximum, double travelTime)
    {
        if (maximum <= minimum)
        {
            position = (minimum + maximum) / 2;
            velocity = 0;
            return;
        }

        position = Math.Clamp(position, minimum, maximum);
        if ((position <= minimum && velocity < 0) || (position >= maximum && velocity > 0))
            velocity = -velocity;

        var span = maximum - minimum;
        var unfolded = position - minimum + velocity * travelTime;
        var folded = PositiveModulo(unfolded, 2 * span);
        if (folded > span)
        {
            position = minimum + 2 * span - folded;
            velocity = -velocity;
        }
        else
        {
            position = minimum + folded;
        }

        // Exact boundary hits should already point inward on the next frame.
        if ((position <= minimum && velocity < 0) || (position >= maximum && velocity > 0))
            velocity = -velocity;
    }

    private static double PositiveModulo(double value, double modulus) => (value % modulus + modulus) % modulus;
    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0;
}
