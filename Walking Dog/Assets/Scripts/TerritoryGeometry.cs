using System;
using System.Collections.Generic;
using Clipper2Lib;

// Projected coordinates retain centimetre precision for clipping; displayed areas
// use geographic latitude so Mercator's scale distortion is not counted as land.
public static class TerritoryGeometry
{
    [Serializable] public struct Point
    {
        public double x, y;
        public Point(double x, double y) { this.x = x; this.y = y; }
    }
    [Serializable] public sealed class Ring { public List<Point> points = new List<Point>(); }
    [Serializable] public sealed class Polygon { public List<Ring> rings = new List<Ring>(); }

    public static List<Polygon> Union(List<Polygon> a, List<Polygon> b) => Clip(a, b, ClipType.Union);
    public static List<Polygon> Difference(List<Polygon> a, List<Polygon> b) => Clip(a, b, ClipType.Difference);
    private static List<Polygon> Clip(List<Polygon> a, List<Polygon> b, ClipType operation)
    {
        var engine = new Clipper64(); engine.AddSubject(Paths(a)); engine.AddClip(Paths(b));
        var tree = new PolyTree64();
        if (!engine.Execute(operation, FillRule.NonZero, tree)) throw new InvalidOperationException("Territory geometry could not be merged.");
        var polygons = new List<Polygon>();
        AddChildren(tree, polygons);
        return polygons;
    }
    private static Paths64 Paths(List<Polygon> polygons)
    {
        var paths = new Paths64();
        foreach (var polygon in polygons)
            for (int i = 0; i < polygon.rings.Count; i++)
            {
                var path = new Path64();
                foreach (var p in polygon.rings[i].points) path.Add(new Point64((long)Math.Round(p.x * 100), (long)Math.Round(p.y * 100)));
                if (Clipper.IsPositive(path) != (i == 0)) path.Reverse();
                paths.Add(path);
            }
        return paths;
    }
    private static void AddChildren(PolyPath64 parent, List<Polygon> polygons)
    {
        for (int i = 0; i < parent.Count; i++)
        {
            var outer = parent[i]; var polygon = new Polygon();
            polygon.rings.Add(ToRing(outer.Polygon));
            for (int j = 0; j < outer.Count; j++)
            { polygon.rings.Add(ToRing(outer[j].Polygon)); AddChildren(outer[j], polygons); }
            polygons.Add(polygon);
        }
    }
    private static Ring ToRing(Path64 path)
    {
        var ring = new Ring(); foreach (var p in path) ring.points.Add(new Point(p.X / 100d, p.Y / 100d)); return ring;
    }
    public static double Area(List<Polygon> polygons)
    {
        double total = 0;
        foreach (var polygon in polygons)
            for (int r = 0; r < polygon.rings.Count; r++)
            {
                var points = polygon.rings[r].points; double sum = 0;
                for (int i = 0; i < points.Count; i++)
                {
                    var a = points[i]; var b = points[(i + 1) % points.Count];
                    double latA = 2 * Math.Atan(Math.Exp(a.y / TerritoryCapture.Radius)) - Math.PI / 2;
                    double latB = 2 * Math.Atan(Math.Exp(b.y / TerritoryCapture.Radius)) - Math.PI / 2;
                    sum += (b.x - a.x) / TerritoryCapture.Radius * (Math.Sin(latA) + Math.Sin(latB));
                }
                total += (r == 0 ? 1 : -1) * Math.Abs(sum) * 6371000d * 6371000d / 2;
            }
        return Math.Max(0, total);
    }
    public static List<Polygon> FromTiles(List<TerritoryCapture.Tile> tiles)
    {
        var polygons = new List<Polygon>();
        foreach (var tile in tiles)
        {
            double x = tile.x * 50d, y = tile.y * 50d;
            polygons.Add(new Polygon { rings = new List<Ring> { new Ring { points = new List<Point> {
                new Point(x,y), new Point(x+50,y), new Point(x+50,y+50), new Point(x,y+50) } } } });
        }
        return polygons;
    }
    public static void Validate(List<Polygon> polygons)
    {
        if (polygons == null) throw new ArgumentException("Missing territory geometry.");
        foreach (var polygon in polygons)
        {
            if (polygon?.rings == null || polygon.rings.Count == 0) throw new ArgumentException("Missing territory boundary.");
            foreach (var ring in polygon.rings)
            {
                if (ring?.points == null || ring.points.Count < 3) throw new ArgumentException("Invalid territory boundary.");
                foreach (var p in ring.points)
                    if (!WalkGpsFilter.Finite(p.x) || !WalkGpsFilter.Finite(p.y) || Math.Abs(p.x) > 20037600 || Math.Abs(p.y) > 12932400)
                        throw new ArgumentException("Invalid territory coordinate.");
            }
        }
    }
}
