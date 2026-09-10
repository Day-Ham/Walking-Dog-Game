using System;

// Sensor timestamps, rather than frame times, determine freshness and speed.
// Kept independent of Unity so recorded routes can exercise the same rules in tests.
internal sealed class WalkGpsFilter
{
    internal const double FreshSeconds = 10;
    internal const double GapSeconds = 15;
    private double lastFix;
    private double lastRouteFix;
    private double routeLatitude, routeLongitude;
    private bool hasAnchor;
    private bool breakPending;
    public bool HasGaps { get; private set; }
    public string Status { get; private set; } = "Waiting for GPS";
    public double LastFixTimestamp => lastFix;

    public void Interrupt(string reason)
    {
        if (hasAnchor) { HasGaps = true; breakPending = true; }
        Status = reason;
    }

    public bool Accept(double latitude, double longitude, double accuracy, double timestamp,
        double observedAt, out bool startsSegment)
    {
        startsSegment = false;
        if (!Finite(latitude) || !Finite(longitude) || !Finite(accuracy)
            || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180 || accuracy <= 0
            || !Finite(timestamp) || !Finite(observedAt) || timestamp <= 0)
        { Interrupt("Waiting for valid GPS"); return false; }
        if (observedAt - timestamp > FreshSeconds || timestamp - observedAt > 2)
        { Interrupt("Tracking interrupted — waiting for fresh GPS"); return false; }
        if (accuracy > StepCountAndGpsManager.AccurateGpsThresholdMeters)
        { Interrupt("Weak GPS — move to an open area"); return false; }
        if (timestamp <= lastFix) return false;
        if (lastFix > 0 && timestamp - lastFix > GapSeconds)
            Interrupt("Tracking interrupted");
        lastFix = timestamp;

        var distance = hasAnchor ? Distance(routeLatitude, routeLongitude, latitude, longitude) : 0;
        if (hasAnchor && !breakPending)
        {
            var seconds = timestamp - lastRouteFix;
            // A small allowance tolerates GPS noise, without the old 100 m floor.
            if (distance > 6 * seconds + 5)
            { Interrupt("Tracking interrupted — GPS jump ignored"); return false; }
            if (distance < Math.Max(3, accuracy * 0.75))
            { Status = "Recording"; return false; }
        }
        startsSegment = !hasAnchor || breakPending;
        routeLatitude = latitude;
        routeLongitude = longitude;
        lastRouteFix = timestamp;
        hasAnchor = true;
        breakPending = false;
        Status = "Recording";
        return true;
    }

    public void Restore(bool hadPoints, bool hadGaps)
    {
        hasAnchor = hadPoints;
        HasGaps = hadGaps || hadPoints;
        breakPending = hadPoints;
        Status = "Waiting for GPS after recovery";
    }

    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        const double radians = Math.PI / 180;
        var a = Math.Sin((lat2 - lat1) * radians / 2);
        var b = Math.Sin((lon2 - lon1) * radians / 2);
        var h = a * a + Math.Cos(lat1 * radians) * Math.Cos(lat2 * radians) * b * b;
        return 6371000 * 2 * Math.Asin(Math.Sqrt(Math.Min(1, Math.Max(0, h))));
    }
}
