using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// One atomic ledger contains both ownership and processed walk IDs.
public sealed class LocalTerritoryRepository
{
    [Serializable]
    public sealed class Claim
    {
        public string walkId;
        public string message;
        public int enclosedTiles;
        public List<TerritoryCapture.Tile> newTiles = new List<TerritoryCapture.Tile>();
        public double newAreaSquareMeters;
        public List<TerritoryGeometry.Polygon> newPolygons = new List<TerritoryGeometry.Polygon>();
    }
    [Serializable]
    private sealed class Ledger
    {
        public int version = 2;
        public string owner;
        public List<Claim> claims = new List<Claim>();
    }

    private readonly string owner, filePath;
    private Ledger ledger;
    private bool loadedBackup;
    private readonly Dictionary<string, Claim> claims = new Dictionary<string, Claim>();
    private readonly HashSet<TerritoryCapture.Tile> owned = new HashSet<TerritoryCapture.Tile>();
    private readonly List<TerritoryCapture.Tile> tiles = new List<TerritoryCapture.Tile>();
    public IReadOnlyList<TerritoryCapture.Tile> Tiles => tiles;
    private List<TerritoryGeometry.Polygon> polygons = new List<TerritoryGeometry.Polygon>();
    public IReadOnlyList<TerritoryGeometry.Polygon> Polygons => polygons;
    public double AreaSquareMeters => TerritoryGeometry.Area(polygons);
    public int Revision => claims.Count;
    public string FilePath => filePath;

    public LocalTerritoryRepository(string directory, string owner, Func<string, StepCountAndGpsManager.SavedWalkSession> findWalk = null)
    {
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Territories require an authenticated owner.");
        this.owner = owner;
        using (var hash = SHA256.Create())
            filePath = Path.Combine(directory, "territory_" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(owner))).Replace("-", "").ToLowerInvariant() + ".json");
        if (!File.Exists(filePath) && !File.Exists(filePath + ".bak")) ledger = new Ledger { owner = owner };
        else
        {
            try { ledger = Read(filePath); }
            catch (NotSupportedException) { throw; }
            catch (Exception) { ledger = Read(filePath + ".bak"); loadedBackup = true; }
        }
        if (ledger.version == 1) Migrate(findWalk);
        foreach (var claim in ledger.claims)
        {
            claims.Add(claim.walkId, claim);
            foreach (var tile in claim.newTiles) if (owned.Add(tile)) tiles.Add(tile);
            polygons = TerritoryGeometry.Union(polygons, claim.newPolygons);
        }
    }

    public Claim Find(string walkId) => walkId != null && claims.TryGetValue(walkId, out var claim) ? claim : null;

    public Claim Apply(StepCountAndGpsManager.SavedWalkSession savedWalk)
    {
        if (savedWalk == null || savedWalk.ownerUserId != owner || string.IsNullOrWhiteSpace(savedWalk.id)
            || string.IsNullOrEmpty(savedWalk.endedAtUtc)) throw new ArgumentException("A completed walk belonging to this player is required.");
        var previous = Find(savedWalk.id);
        if (previous != null) return previous;
        var result = TerritoryCapture.EvaluateLoop(savedWalk);
        var claim = new Claim { walkId = savedWalk.id, message = result.message.Replace("No tiles claimed", "No territory claimed") };
        claim.newPolygons = TerritoryGeometry.Difference(result.polygons, polygons);
        claim.newAreaSquareMeters = TerritoryGeometry.Area(claim.newPolygons);
        if (result.Accepted) claim.message = AreaMessage(claim.newAreaSquareMeters);
        var nextPolygons = TerritoryGeometry.Union(polygons, claim.newPolygons);
        var next = new Ledger { owner = owner, claims = new List<Claim>(ledger.claims) { claim } };
        Write(next); // Do not mutate the in-memory award until durable commit succeeds.
        ledger = next;
        claims.Add(claim.walkId, claim);
        polygons = nextPolygons;
        return claim;
    }

    private static string AreaMessage(double area) => area < 0.5
        ? "Loop completed! You already own the area inside this route."
        : "+" + area.ToString("N0") + " m² of new territory claimed!";

    private void Migrate(Func<string, StepCountAndGpsManager.SavedWalkSession> findWalk)
    {
        var accumulated = new List<TerritoryGeometry.Polygon>();
        var next = new Ledger { owner = owner };
        foreach (var old in ledger.claims)
        {
            var shape = TerritoryGeometry.FromTiles(old.newTiles);
            var walk = findWalk?.Invoke(old.walkId);
            if (old.enclosedTiles > 0 && walk != null && walk.ownerUserId == owner)
            {
                var loop = TerritoryCapture.EvaluateLoop(walk);
                if (loop.Accepted) shape = loop.polygons;
            }
            old.newPolygons = TerritoryGeometry.Difference(shape, accumulated);
            old.newAreaSquareMeters = TerritoryGeometry.Area(old.newPolygons);
            old.message = old.enclosedTiles > 0 ? AreaMessage(old.newAreaSquareMeters) : old.message.Replace("No tiles claimed", "No territory claimed");
            accumulated = TerritoryGeometry.Union(accumulated, old.newPolygons);
            next.claims.Add(old);
        }
        Write(next); // Atomic upgrade retains the previous ledger as backup.
        ledger = next;
    }

    private Ledger Read(string path)
    {
        var data = JsonUtility.FromJson<Ledger>(File.ReadAllText(path));
        if (data == null) throw new InvalidDataException("Territory ledger is empty.");
        if (data.version != 1 && data.version != 2) throw new NotSupportedException("Unsupported territory version.");
        if (data.owner != owner || data.claims == null) throw new InvalidDataException("Territory owner mismatch.");
        var ids = new HashSet<string>();
        var seen = new HashSet<TerritoryCapture.Tile>();
        foreach (var claim in data.claims)
        {
            if (claim == null || string.IsNullOrWhiteSpace(claim.walkId) || !ids.Add(claim.walkId) || string.IsNullOrEmpty(claim.message)
                || claim.newTiles == null || claim.enclosedTiles < claim.newTiles.Count || claim.enclosedTiles > TerritoryCapture.MaxTiles)
                throw new InvalidDataException("Invalid territory claim.");
            foreach (var tile in claim.newTiles)
                if (Math.Abs((long)tile.x) > 400751 || Math.Abs((long)tile.y) > 258646 || !seen.Add(tile))
                    throw new InvalidDataException("Invalid territory tile.");
            if (data.version == 2)
            {
                TerritoryGeometry.Validate(claim.newPolygons);
                if (!WalkGpsFilter.Finite(claim.newAreaSquareMeters) || claim.newAreaSquareMeters < 0
                    || Math.Abs(TerritoryGeometry.Area(claim.newPolygons) - claim.newAreaSquareMeters) > 0.1)
                    throw new InvalidDataException("Invalid territory area.");
            }
        }
        return data;
    }

    private void Write(Ledger data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
        string temporary = filePath + ".tmp";
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(filePath)) File.Replace(temporary, filePath, loadedBackup ? null : filePath + ".bak");
            else File.Move(temporary, filePath);
            loadedBackup = false;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
