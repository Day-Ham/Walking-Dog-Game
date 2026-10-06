using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Point = StepCountAndGpsManager.WalkRoutePoint;

namespace WalkingDog.Leaderboards
{
    public sealed class SocialRoute
    {
        public List<Point> Points = new List<Point>();
        public bool IsOwner, IsShared, CanShare;
        public string Message = "";
    }

    public interface ISocialRouteService
    {
        Task<SocialRoute> LoadRouteAsync(string owner, string walkId, CancellationToken token);
        Task SetRouteSharedAsync(string owner, string walkId, bool shared, CancellationToken token);
    }

    // Only coordinates and continuity leave the device while Walk sharing is ON.
    // Sensor timestamps/accuracy and recovery data stay local.
    internal static class SocialRouteCodec
    {
        internal const int MaxPoints = 2000, MaxJsonLength = 200000;
        [Serializable] private sealed class Payload { public List<Sample> points = new List<Sample>(); }
        [Serializable] private sealed class Sample { public float lat, lng; public bool gap; }

        internal static string Encode(IReadOnlyList<Point> source)
        {
            if (source == null || source.Count == 0) throw new InvalidOperationException("This walk has no saved GPS route.");
            // Preserve endpoints on both sides of every GPS gap while reducing
            // long routes. Never invent a line across an unrecorded segment.
            var required = new SortedSet<int> { 0, source.Count - 1 };
            for (int i = 0; i < source.Count; i++)
            {
                Validate(source[i]);
                if (source[i].startsNewSegment) { required.Add(i); if (i > 0) required.Add(i - 1); }
            }
            if (required.Count > MaxPoints) throw new InvalidOperationException("This route has too many GPS breaks to share.");
            int budget = MaxPoints - required.Count;
            if (budget > 0)
            {
                int stride = Math.Max(1, (int)Math.Ceiling(source.Count / (double)budget));
                for (int i = 0; i < source.Count && required.Count < MaxPoints; i += stride) required.Add(i);
            }
            var data = new Payload();
            foreach (int index in required)
            {
                var point = source[index];
                data.points.Add(new Sample { lat = point.latitude, lng = point.longitude, gap = index == 0 || point.startsNewSegment });
            }
            var json = JsonUtility.ToJson(data);
            if (json.Length > MaxJsonLength) throw new InvalidOperationException("This route is too large to share.");
            return json;
        }

        internal static List<Point> Decode(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > MaxJsonLength) throw new FormatException("Invalid shared route.");
            var data = JsonUtility.FromJson<Payload>(json);
            if (data?.points == null || data.points.Count == 0 || data.points.Count > MaxPoints) throw new FormatException("Invalid shared route.");
            var points = new List<Point>();
            foreach (var sample in data.points)
            {
                if (sample == null) throw new FormatException("Invalid shared route point.");
                var point = new Point { latitude = sample.lat, longitude = sample.lng, startsNewSegment = points.Count == 0 || sample.gap };
                Validate(point); points.Add(point);
            }
            return points;
        }

        private static void Validate(Point point)
        {
            if (point == null || float.IsNaN(point.latitude) || float.IsNaN(point.longitude)
                || float.IsInfinity(point.latitude) || float.IsInfinity(point.longitude)
                || Math.Abs(point.latitude) > 85 || Math.Abs(point.longitude) > 180)
                throw new FormatException("This route contains unsupported GPS coordinates.");
        }

        internal static List<Point> OwnedLocalRoute(LocalWalkRepository repository, string owner, string walkId)
        {
            var walk = repository?.Find(walkId);
            return walk != null && walk.ownerUserId == owner && walk.routePoints != null
                ? walk.routePoints.Select(p => p.Clone()).ToList() : new List<Point>();
        }
    }
}
