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
    }
    [Serializable]
    private sealed class Ledger
    {
        public int version = 1;
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
    public int Revision => claims.Count;
    public string FilePath => filePath;

    public LocalTerritoryRepository(string directory, string owner)
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
        foreach (var claim in ledger.claims)
        {
            claims.Add(claim.walkId, claim);
            foreach (var tile in claim.newTiles) if (owned.Add(tile)) tiles.Add(tile);
        }
    }

    public Claim Find(string walkId) => walkId != null && claims.TryGetValue(walkId, out var claim) ? claim : null;

    public Claim Apply(StepCountAndGpsManager.SavedWalkSession savedWalk)
    {
        if (savedWalk == null || savedWalk.ownerUserId != owner || string.IsNullOrWhiteSpace(savedWalk.id)
            || string.IsNullOrEmpty(savedWalk.endedAtUtc)) throw new ArgumentException("A completed walk belonging to this player is required.");
        var previous = Find(savedWalk.id);
        if (previous != null) return previous;
        var result = TerritoryCapture.Evaluate(savedWalk);
        var claim = new Claim { walkId = savedWalk.id, enclosedTiles = result.tiles.Count, message = result.message };
        foreach (var tile in result.tiles) if (!owned.Contains(tile)) claim.newTiles.Add(tile);
        if (result.Accepted) claim.message = claim.newTiles.Count == 0
            ? "Loop completed! You already own all " + result.tiles.Count + " tiles inside it."
            : "+" + claim.newTiles.Count + " new " + (claim.newTiles.Count == 1 ? "tile" : "tiles") + " claimed!" +
              (result.tiles.Count > claim.newTiles.Count ? " " + (result.tiles.Count - claim.newTiles.Count) + " already owned." : "");
        var next = new Ledger { owner = owner, claims = new List<Claim>(ledger.claims) { claim } };
        Write(next); // Do not mutate the in-memory award until durable commit succeeds.
        ledger = next;
        claims.Add(claim.walkId, claim);
        foreach (var tile in claim.newTiles) { owned.Add(tile); tiles.Add(tile); }
        return claim;
    }

    private Ledger Read(string path)
    {
        var data = JsonUtility.FromJson<Ledger>(File.ReadAllText(path));
        if (data == null) throw new InvalidDataException("Territory ledger is empty.");
        if (data.version != 1) throw new NotSupportedException("Unsupported territory version.");
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
