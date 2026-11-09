using System;

namespace BonbonPet.Core;

/// <summary>Estimates a release velocity in the caller's coordinate unit per second.</summary>
public sealed class PointerVelocity
{
    private const int Capacity = 128;
    private const double WindowSeconds = .09;
    private const double StationarySeconds = .095;
    private readonly Sample[] _samples = new Sample[Capacity];
    private int _first, _count;
    private double _lastMotionTime = double.NegativeInfinity;

    public void Reset(double timeSeconds, double x, double y)
    {
        _first = _count = 0;
        _lastMotionTime = double.NegativeInfinity;
        Add(timeSeconds, x, y);
    }

    public void Add(double timeSeconds, double x, double y)
    {
        if (!double.IsFinite(timeSeconds) || !double.IsFinite(x) || !double.IsFinite(y)) return;
        if (_count > 0)
        {
            int previousIndex = (_first + _count - 1) % Capacity;
            var previous = _samples[previousIndex];
            if (timeSeconds < previous.Time) return;
            bool moved = x != previous.X || y != previous.Y;
            if (moved) _lastMotionTime = timeSeconds;
            if (timeSeconds == previous.Time)
            {
                // Mouse events can share a clock tick. Keep the final position;
                // a zero-duration pair must never create infinite velocity.
                _samples[previousIndex] = new Sample(timeSeconds, x, y);
                return;
            }
        }
        if (_count == Capacity) { _first = (_first + 1) % Capacity; _count--; }
        _samples[(_first + _count) % Capacity] = new Sample(timeSeconds, x, y);
        _count++;
    }

    public (double X, double Y) Estimate(double releaseTimeSeconds)
    {
        if (_count < 2 || !double.IsFinite(releaseTimeSeconds) || releaseTimeSeconds - _lastMotionTime >= StationarySeconds)
            return (0, 0);
        var latest = At(_count - 1);
        if (releaseTimeSeconds < latest.Time - .000001 || releaseTimeSeconds - latest.Time >= StationarySeconds) return (0, 0);
        releaseTimeSeconds = Math.Max(releaseTimeSeconds, latest.Time);

        int start = _count - 1;
        double cutoff = releaseTimeSeconds - WindowSeconds;
        while (start > 0 && At(start - 1).Time >= cutoff) start--;
        // At a low event rate, retain one preceding sample for a real elapsed
        // interval rather than inventing a frame duration or returning zero.
        if (start == _count - 1 && start > 0 && latest.Time - At(start - 1).Time <= .15) start--;
        if (start == _count - 1) return (0, 0);

        double weightSum = 0, tSum = 0, xSum = 0, ySum = 0;
        for (int i = start; i < _count; i++)
        {
            var sample = At(i);
            double t = sample.Time - latest.Time;
            double weight = Weight(i, start, latest.Time);
            weightSum += weight; tSum += weight * t; xSum += weight * sample.X; ySum += weight * sample.Y;
        }
        double meanT = tSum / weightSum, meanX = xSum / weightSum, meanY = ySum / weightSum;
        double variance = 0, covarianceX = 0, covarianceY = 0;
        for (int i = start; i < _count; i++)
        {
            var sample = At(i);
            double t = sample.Time - latest.Time - meanT;
            double weight = Weight(i, start, latest.Time);
            variance += weight * t * t;
            covarianceX += weight * t * (sample.X - meanX);
            covarianceY += weight * t * (sample.Y - meanY);
        }
        if (variance < 1e-12) return (0, 0);
        double vx = covarianceX / variance, vy = covarianceY / variance;
        return double.IsFinite(vx) && double.IsFinite(vy) ? (vx, vy) : (0, 0);
    }

    private double Weight(int index, int start, double latestTime)
    {
        var sample = At(index);
        double previousTime = index > start ? At(index - 1).Time : sample.Time;
        double nextTime = index + 1 < _count ? At(index + 1).Time : sample.Time;
        // Time coverage prevents a burst of high-rate mouse events from
        // overwhelming the rest of the trajectory. Recency favors the flick.
        double coverage = Math.Clamp((nextTime - previousTime) * .5, .0005, .05);
        return coverage * Math.Exp((sample.Time - latestTime) / .035);
    }

    private Sample At(int index) => _samples[(_first + index) % Capacity];
    private readonly record struct Sample(double Time, double X, double Y);
}
