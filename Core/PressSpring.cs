using System;

namespace BonbonPet.Core;

/// <summary>Held compression followed by a damped spring release.</summary>
public sealed class PressSpring
{
    public bool IsPressed { get; private set; }
    public double Compression { get; private set; }
    private double _velocity;

    private static double Target(double strength) => Math.Clamp(double.IsFinite(strength) ? strength : 0, 0, 3) * .16;
    public void Press(double strength)
    {
        IsPressed = true;
        Compression = Math.Max(Compression, Target(strength) * .65);
        _velocity = 0;
    }
    public void Release() { IsPressed = false; _velocity = 0; }
    public void Step(double dt, double strength)
    {
        if (!double.IsFinite(dt) || dt <= 0) return;
        dt = Math.Min(dt, .1);
        if (strength <= 0) { Compression = _velocity = 0; return; }
        if (IsPressed)
        {
            Compression += (Target(strength) - Compression) * (1 - Math.Exp(-32 * dt));
            _velocity = 0;
            return;
        }
        int steps = Math.Max(1, (int)Math.Ceiling(dt * 240));
        double step = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            _velocity += (-625 * Compression - 15 * _velocity) * step;
            Compression += _velocity * step;
        }
        if (Math.Abs(Compression) < .0003 && Math.Abs(_velocity) < .004) Compression = _velocity = 0;
    }
}
