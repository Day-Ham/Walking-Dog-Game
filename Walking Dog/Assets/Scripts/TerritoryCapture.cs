using System;
using System.Collections.Generic;

// Version 1: personal square tiles on a fixed Web Mercator grid.
public static class TerritoryCapture
{
    public const int Version = 1;
    public const double TileSize = 50;
    public const double Radius = 6378137;
    // A loop may close when its final accepted GPS sample is this close to its first one.
    // The live walk HUD uses this same value so it never advertises a different range.
    public const double ClosureMeters = 50;
    public const int MaxTiles = 1000;

    [Serializable]
    public struct Tile : IEquatable<Tile>
    {
        public int x, y;
        public Tile(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Tile other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is Tile other && Equals(other);
        public override int GetHashCode() => unchecked(x * 397 ^ y);
    }

    public sealed class Result
    {
        public string message;
        public readonly List<Tile> tiles = new List<Tile>();
        public bool Accepted => tiles.Count > 0;
    }

    private struct Point
    {
        public double x, y;
        public Point(double x, double y) { this.x = x; this.y = y; }
    }

    public static Result Evaluate(StepCountAndGpsManager.SavedWalkSession walk)
    {
        var result = new Result();
        if (walk == null || walk.territoryVersion != Version || walk.trackingVersion != 1)
            return Reject(result, "Territories are available for new walks recorded with this update.");
        if (walk.hasTrackingGaps) return Reject(result, "No tiles claimed: GPS gaps interrupted the loop.");
        var samples = walk.routePoints;
        if (samples == null || samples.Count < 4) return Reject(result, "No tiles claimed: record a larger loop with a clear GPS signal.");
        if (samples.Count > 6000) return Reject(result, "No tiles claimed: this route exceeds the first version's size limit.");
        var points = new List<Point>();
        double distance = 0, previousTimestamp = 0, previousElapsed = -1;
        for (int i = 0; i < samples.Count; i++)
        {
            var p = samples[i];
            if (p == null || !Finite(p.latitude) || !Finite(p.longitude) || Math.Abs(p.latitude) > 75 || Math.Abs(p.longitude) > 180
                || !Finite(p.accuracyMeters) || p.accuracyMeters <= 0 || p.accuracyMeters > StepCountAndGpsManager.AccurateGpsThresholdMeters
                || !Finite(p.gpsTimestamp) || p.gpsTimestamp <= previousTimestamp
                || !Finite(p.secondsSinceSessionStart) || p.secondsSinceSessionStart < 0 || p.secondsSinceSessionStart < previousElapsed
                || p.secondsSinceSessionStart > walk.durationSeconds)
                return Reject(result, "No tiles claimed: the GPS route could not be verified.");
            if (i > 0 && p.startsNewSegment) return Reject(result, "No tiles claimed: GPS gaps interrupted the loop.");
            if (i > 0)
            {
                double segment = Distance(samples[i - 1], p);
                if (segment > 6 * (p.gpsTimestamp - previousTimestamp) + 5)
                    return Reject(result, "No tiles claimed: an unexpected GPS jump interrupted the loop.");
                distance += segment;
            }
            previousTimestamp = p.gpsTimestamp;
            previousElapsed = p.secondsSinceSessionStart;
            var point = Project(p.latitude, p.longitude);
            if (points.Count == 0 || Length(points[points.Count - 1], point) > 0.01) points.Add(point);
        }
        if (Distance(samples[0], samples[samples.Count - 1]) > ClosureMeters)
            return Reject(result, $"No tiles claimed: finish within {ClosureMeters:0} m of where you started to close the loop.");
        if (distance < 200) return Reject(result, "No tiles claimed: walk at least 200 m around a loop.");
        if (points.Count > 1 && Length(points[0], points[points.Count - 1]) < 0.01) points.RemoveAt(points.Count - 1);
        if (points.Count < 3) return Reject(result, "No tiles claimed: the route needs to enclose an area.");

        double minX = points[0].x, maxX = minX, minY = points[0].y, maxY = minY, area = 0;
        var origin = points[0];
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i]; var b = points[(i + 1) % points.Count];
            minX = Math.Min(minX, a.x); maxX = Math.Max(maxX, a.x);
            minY = Math.Min(minY, a.y); maxY = Math.Max(maxY, a.y);
            area += (a.x - origin.x) * (b.y - origin.y) - (b.x - origin.x) * (a.y - origin.y);
        }
        int left = (int)Math.Floor(minX / TileSize), right = (int)Math.Floor(maxX / TileSize);
        int bottom = (int)Math.Floor(minY / TileSize), top = (int)Math.Floor(maxY / TileSize);
        if (maxX - minX > 5000 || maxY - minY > 5000 || (long)(right - left + 1) * (top - bottom + 1) > 10000)
            return Reject(result, "No tiles claimed: this loop is too large for the first version.");
        // Reject crossing/touching non-adjacent edges, including the short closing edge.
        // Bounding boxes avoid most expensive intersection checks on dense GPS routes.
        for (int i = 0; i < points.Count; i++)
            for (int j = i + 2; j < points.Count; j++)
            {
                if (i == 0 && j == points.Count - 1) continue;
                if (Intersects(points[i], points[(i + 1) % points.Count], points[j], points[(j + 1) % points.Count]))
                    return Reject(result, "No tiles claimed: use a simple loop without crossing or retracing your route.");
            }
        double scale = Math.Cos(samples[0].latitude * Math.PI / 180);
        if (Math.Abs(area) * 0.5 * scale * scale < 2500)
            return Reject(result, "No tiles claimed: enclose a larger area (at least 2,500 m²).");
        for (int x = left; x <= right; x++)
            for (int y = bottom; y <= top; y++)
                if (Contains(points, new Point((x + 0.5) * TileSize, (y + 0.5) * TileSize)))
                {
                    result.tiles.Add(new Tile(x, y));
                    if (result.tiles.Count > MaxTiles) { result.tiles.Clear(); return Reject(result, "No tiles claimed: this loop contains too many tiles for the first version."); }
                }
        result.message = result.Accepted ? "Loop completed." : "No tiles claimed: the loop did not enclose a tile centre. Try a wider loop.";
        return result;
    }

    private static Result Reject(Result result, string message) { result.message = message; return result; }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static Point Project(double lat, double lng) => new Point(Radius * lng * Math.PI / 180, Radius * Math.Log(Math.Tan(Math.PI / 4 + lat * Math.PI / 360)));
    private static double Length(Point a, Point b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
    private static double Distance(StepCountAndGpsManager.WalkRoutePoint a, StepCountAndGpsManager.WalkRoutePoint b)
    {
        double lat = (b.latitude - a.latitude) * Math.PI / 180, lng = (b.longitude - a.longitude) * Math.PI / 180;
        double h = Math.Sin(lat / 2) * Math.Sin(lat / 2) + Math.Cos(a.latitude * Math.PI / 180) * Math.Cos(b.latitude * Math.PI / 180) * Math.Sin(lng / 2) * Math.Sin(lng / 2);
        return 6371000 * 2 * Math.Asin(Math.Sqrt(Math.Min(1, h)));
    }
    private static double Cross(Point a, Point b, Point c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    private static bool Intersects(Point a, Point b, Point c, Point d)
    {
        if (Math.Max(a.x, b.x) < Math.Min(c.x, d.x) || Math.Max(c.x, d.x) < Math.Min(a.x, b.x)
            || Math.Max(a.y, b.y) < Math.Min(c.y, d.y) || Math.Max(c.y, d.y) < Math.Min(a.y, b.y)) return false;
        return Cross(a, b, c) * Cross(a, b, d) <= 0 && Cross(c, d, a) * Cross(c, d, b) <= 0;
    }
    private static bool Contains(List<Point> polygon, Point p)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i]; var b = polygon[j];
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
    }
}
