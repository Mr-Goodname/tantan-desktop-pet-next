using System;
using System.Windows;

namespace BonbonPet.Core;

/// <summary>Allocation-free contacts between transformed convex sprite outlines.</summary>
public static class PeerCollision
{
    private const double Restitution = .9;
    private const double ContactTolerance = 1e-7;
    private const double SeparationPadding = .001;

    public static bool Resolve(PetPhysics a, ReadOnlySpan<Point> verticesA, bool movableA,
        PetPhysics b, ReadOnlySpan<Point> verticesB, bool movableB, double massA = 1, double massB = 1)
    {
        if (!TryBounds(verticesA, out var boundsA) || !TryBounds(verticesB, out var boundsB) ||
            !Intersects(boundsA, boundsB) ||
            !FindContact(verticesA, verticesB, out var normal, out double penetration)) return false;

        double inverseA = InverseMass(movableA, massA), inverseB = InverseMass(movableB, massB);
        double inverseSum = inverseA + inverseB;
        if (inverseSum == 0) return true;
        double correction = Math.Max(0, penetration) + SeparationPadding;
        a.X -= normal.X * correction * inverseA / inverseSum;
        a.Y -= normal.Y * correction * inverseA / inverseSum;
        b.X += normal.X * correction * inverseB / inverseSum;
        b.Y += normal.Y * correction * inverseB / inverseSum;
        ApplyImpulse(a, movableA, inverseA, b, movableB, inverseB, normal);
        return true;
    }

    /// <summary>
    /// Sweeps the current convex outlines from the preceding centers to their
    /// current centers. Rotation/shape changes are sampled once per simulation
    /// step; translation is continuous, including fast mouse throws.
    /// </summary>
    public static bool ResolveSwept(PetPhysics a, ReadOnlySpan<Point> verticesA, bool movableA, Point previousCenterA,
        PetPhysics b, ReadOnlySpan<Point> verticesB, bool movableB, Point previousCenterB,
        double elapsedSeconds, double massA = 1, double massB = 1)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0 ||
            !Finite(previousCenterA) || !Finite(previousCenterB))
            return Resolve(a, verticesA, movableA, b, verticesB, movableB, massA, massB);
        if (!TryBounds(verticesA, out var boundsA) || !TryBounds(verticesB, out var boundsB)) return false;

        // A held or fixed pet is an immovable obstacle at its displayed location.
        Vector movementA = movableA ? new Vector(a.X - previousCenterA.X, a.Y - previousCenterA.Y) : default;
        Vector movementB = movableB ? new Vector(b.X - previousCenterB.X, b.Y - previousCenterB.Y) : default;
        if (!Finite(movementA) || !Finite(movementB) ||
            !Intersects(SweptBounds(boundsA, movementA), SweptBounds(boundsB, movementB)) ||
            !FindSweep(verticesA, movementA, verticesB, movementB, out double hitTime, out var normal))
            return Resolve(a, verticesA, movableA, b, verticesB, movableB, massA, massB);

        double inverseA = InverseMass(movableA, massA), inverseB = InverseMass(movableB, massB);
        if (inverseA + inverseB == 0 || RelativeNormalSpeed(a, movableA, b, movableB, normal) >= 0)
            return Resolve(a, verticesA, movableA, b, verticesB, movableB, massA, massB);

        if (movableA) { a.X -= movementA.X * (1 - hitTime); a.Y -= movementA.Y * (1 - hitTime); }
        if (movableB) { b.X -= movementB.X * (1 - hitTime); b.Y -= movementB.Y * (1 - hitTime); }
        ApplyImpulse(a, movableA, inverseA, b, movableB, inverseB, normal);
        double remaining = Math.Min(elapsedSeconds, .1) * (1 - hitTime);
        double inverseSum = inverseA + inverseB;
        if (movableA)
        {
            a.X += a.VelocityX * remaining - normal.X * SeparationPadding * inverseA / inverseSum;
            a.Y += a.VelocityY * remaining - normal.Y * SeparationPadding * inverseA / inverseSum;
        }
        if (movableB)
        {
            b.X += b.VelocityX * remaining + normal.X * SeparationPadding * inverseB / inverseSum;
            b.Y += b.VelocityY * remaining + normal.Y * SeparationPadding * inverseB / inverseSum;
        }
        return true;
    }

    private static bool FindContact(ReadOnlySpan<Point> a, ReadOnlySpan<Point> b, out Vector normal, out double penetration)
    {
        normal = default; penetration = double.PositiveInfinity;
        for (int polygon = 0; polygon < 2; polygon++)
        {
            var edges = polygon == 0 ? a : b;
            for (int i = 0; i < edges.Length; i++)
            {
                if (!TryNormal(edges[i], edges[(i + 1) % edges.Length], out var axis)) continue;
                Project(a, axis, out double minA, out double maxA);
                Project(b, axis, out double minB, out double maxB);
                double positive = maxA - minB, negative = maxB - minA;
                if (positive < -ContactTolerance || negative < -ContactTolerance) return false;
                // Endpoint distances also handle one interval containing the
                // other; intersected interval length alone under-corrects it.
                double depth = Math.Min(positive, negative);
                if (depth < penetration)
                {
                    penetration = depth;
                    normal = positive <= negative ? axis : -axis;
                }
            }
        }
        return double.IsFinite(penetration);
    }

    private static bool FindSweep(ReadOnlySpan<Point> a, Vector movementA, ReadOnlySpan<Point> b, Vector movementB,
        out double hitTime, out Vector normal)
    {
        hitTime = double.NegativeInfinity; normal = default;
        double exitTime = double.PositiveInfinity;
        bool initiallyPenetrating = true;
        for (int polygon = 0; polygon < 2; polygon++)
        {
            var edges = polygon == 0 ? a : b;
            for (int i = 0; i < edges.Length; i++)
            {
                if (!TryNormal(edges[i], edges[(i + 1) % edges.Length], out var axis)) continue;
                Project(a, axis, out double minA, out double maxA);
                Project(b, axis, out double minB, out double maxB);
                double offsetA = Vector.Multiply(movementA, axis), offsetB = Vector.Multiply(movementB, axis);
                minA -= offsetA; maxA -= offsetA; minB -= offsetB; maxB -= offsetB;
                if (Math.Min(maxA, maxB) - Math.Max(minA, minB) <= ContactTolerance) initiallyPenetrating = false;
                double relative = offsetB - offsetA;
                if (Math.Abs(relative) < 1e-12)
                {
                    if (maxA < minB - ContactTolerance || maxB < minA - ContactTolerance) return false;
                    continue;
                }
                double first = (minA - maxB) / relative, second = (maxA - minB) / relative;
                double enter = Math.Min(first, second), leave = Math.Max(first, second);
                if (enter > hitTime) { hitTime = enter; normal = relative < 0 ? axis : -axis; }
                exitTime = Math.Min(exitTime, leave);
                if (hitTime > exitTime + ContactTolerance) return false;
            }
        }
        // Already-overlapping bodies use the static MTV. Replaying an old
        // contact while they separate would produce repeated bounces.
        if (initiallyPenetrating || !double.IsFinite(hitTime) || hitTime < -ContactTolerance ||
            hitTime > 1 + ContactTolerance || exitTime < 0) return false;
        hitTime = Math.Clamp(hitTime, 0, 1);
        return true;
    }

    private static void ApplyImpulse(PetPhysics a, bool movableA, double inverseA,
        PetPhysics b, bool movableB, double inverseB, Vector normal)
    {
        double relative = RelativeNormalSpeed(a, movableA, b, movableB, normal);
        if (relative >= 0) return;
        double impulse = -(1 + Restitution) * relative / (inverseA + inverseB);
        if (movableA) { a.VelocityX -= impulse * inverseA * normal.X; a.VelocityY -= impulse * inverseA * normal.Y; }
        if (movableB) { b.VelocityX += impulse * inverseB * normal.X; b.VelocityY += impulse * inverseB * normal.Y; }
    }

    private static double RelativeNormalSpeed(PetPhysics a, bool movableA, PetPhysics b, bool movableB, Vector normal) =>
        ((movableB ? b.VelocityX : 0) - (movableA ? a.VelocityX : 0)) * normal.X +
        ((movableB ? b.VelocityY : 0) - (movableA ? a.VelocityY : 0)) * normal.Y;

    private static double InverseMass(bool movable, double mass) =>
        movable ? 1 / (double.IsFinite(mass) && mass > 0 ? Math.Clamp(mass, 1e-9, 1e9) : 1) : 0;

    private static bool TryNormal(Point start, Point end, out Vector normal)
    {
        normal = new Vector(start.Y - end.Y, end.X - start.X);
        double length = normal.Length;
        if (length <= 1e-12 || !double.IsFinite(length)) return false;
        normal /= length; return true;
    }

    private static void Project(ReadOnlySpan<Point> vertices, Vector axis, out double min, out double max)
    {
        min = double.PositiveInfinity; max = double.NegativeInfinity;
        foreach (var point in vertices)
        {
            double value = point.X * axis.X + point.Y * axis.Y;
            min = Math.Min(min, value); max = Math.Max(max, value);
        }
    }

    private static bool TryBounds(ReadOnlySpan<Point> vertices, out Rect bounds)
    {
        bounds = Rect.Empty;
        if (vertices.Length < 3) return false;
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var point in vertices)
        {
            if (!Finite(point)) return false;
            minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y); maxY = Math.Max(maxY, point.Y);
        }
        if (maxX - minX <= ContactTolerance || maxY - minY <= ContactTolerance) return false;
        bounds = new Rect(minX, minY, maxX - minX, maxY - minY); return true;
    }

    private static Rect SweptBounds(Rect bounds, Vector movement) => new(
        bounds.X - Math.Max(0, movement.X), bounds.Y - Math.Max(0, movement.Y),
        bounds.Width + Math.Abs(movement.X), bounds.Height + Math.Abs(movement.Y));

    private static bool Intersects(Rect a, Rect b) => a.Left <= b.Right + ContactTolerance &&
        a.Right + ContactTolerance >= b.Left && a.Top <= b.Bottom + ContactTolerance && a.Bottom + ContactTolerance >= b.Top;

    private static bool Finite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    private static bool Finite(Vector vector) => double.IsFinite(vector.X) && double.IsFinite(vector.Y);
}
