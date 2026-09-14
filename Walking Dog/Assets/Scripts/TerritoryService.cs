using System;
using System.Collections.Generic;
using System.IO;

// Replays only new-version completed local walks. This also closes the crash window
// between saving a walk and committing its territory award.
public sealed class TerritoryService
{
    private readonly LocalWalkRepository walks;
    private LocalTerritoryRepository repository;
    private string owner = "", generation = Guid.NewGuid().ToString("N");
    private static readonly TerritoryCapture.Tile[] Empty = new TerritoryCapture.Tile[0];
    public TerritoryService(LocalWalkRepository walks) { this.walks = walks; }
    public string Owner => owner;
    public string Error { get; private set; } = "";
    public IReadOnlyList<TerritoryCapture.Tile> Tiles => repository?.Tiles ?? Empty;
    public string Revision => generation + ":" + (repository?.Revision ?? 0);

    public void Refresh(string authenticatedOwner)
    {
        authenticatedOwner = authenticatedOwner ?? "";
        if (owner != authenticatedOwner)
        { owner = authenticatedOwner; repository = null; generation = Guid.NewGuid().ToString("N"); Error = ""; }
        if (string.IsNullOrWhiteSpace(owner)) return;
        try
        {
            if (repository == null) repository = new LocalTerritoryRepository(Path.Combine(walks.DirectoryPath, "Territories"), owner);
            var completed = walks.LoadAll();
            completed.Sort((a, b) => { int order = string.CompareOrdinal(a.endedAtUtc, b.endedAtUtc); return order != 0 ? order : string.CompareOrdinal(a.id, b.id); });
            foreach (var walk in completed)
                if (walk.ownerUserId == owner && walk.territoryVersion == TerritoryCapture.Version && repository.Find(walk.id) == null)
                    repository.Apply(walk);
            Error = "";
        }
        catch (Exception)
        { Error = "Territory save pending. Your walk is safe; we'll retry automatically."; }
    }

    public string Summary(string walkId, string walkOwner, bool unsaved)
    {
        if (unsaved) return "Save your walk first to check its territory.";
        if (string.IsNullOrWhiteSpace(walkOwner) || walkOwner != owner) return "Sign in to the account that recorded this walk to view its territory.";
        return repository?.Find(walkId)?.message ?? (string.IsNullOrEmpty(Error) ? "This walk is not eligible for territory. Start a new walk to claim a loop." : Error);
    }
}
