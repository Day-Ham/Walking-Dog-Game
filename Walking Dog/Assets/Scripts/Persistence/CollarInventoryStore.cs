using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;
// persistence of gacha item to track which is unlocked or not
public class CollarInventoryStore
{
    public const string UnlockedItemIdsKey = "UnlockedGachaItemIds";
    public const string EquippedItemIdKey = "EquippedGachaItemId";

    private readonly FirebaseFirestore db;

    public CollarInventoryStore(FirebaseFirestore db)
    {
        this.db = db;
    }

    public static HashSet<string> GetLocalUnlockedItemIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        // New cache: IDs are separated with | so an ID cannot be confused with another one.
        AddIds(ids, PlayerPrefs.GetString(UnlockedItemIdsKey, ""), '|');

        // Migration: existing installs used collar display names in this old key.
        AddIds(ids, PlayerPrefs.GetString("UnlockedCollars", ""), ',');
        return ids;
    }

    public async Task UnlockItemAsync(string uid, GachaItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.ItemId))
            throw new ArgumentException("A gacha item needs a permanent Item ID.", nameof(item));

        var localIds = GetLocalUnlockedItemIds();
        localIds.Add(item.ItemId);
        SaveLocal(localIds, item.ItemId, item.CollarColorHex);
       
        //save marker appearance
        EquippedGachaMarkerIcon.Save(item);

        var inventoryRef = db.Document($"users/{uid}/inventory/main");
        var profileRef = db.Document($"leaderboardProfiles/{uid}");

        await db.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(inventoryRef);
            var unlockedIds = ReadItemIds(snapshot);
            unlockedIds.Add(item.ItemId);

            // Firebase stores state only. The client resolves this ID through its GachaItem catalog.
            transaction.Set(inventoryRef, new Dictionary<string, object>
            {
                ["unlockedItemIds"] = new List<string>(unlockedIds),
                ["equippedItemId"] = item.ItemId,
                ["updatedAt"] = FieldValue.ServerTimestamp
            }, SetOptions.MergeAll);

            // Colour remains on the public profile for the existing map/leaderboard appearance.
            transaction.Set(profileRef, new Dictionary<string, object>
            {
                ["equippedItemId"] = item.ItemId,
                ["equippedCollarColor"] = item.CollarColorHex,
                ["updatedAt"] = FieldValue.ServerTimestamp
            }, SetOptions.MergeAll);
        });
    }

    public Task EquipItemAsync(string uid, GachaItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.ItemId))
            throw new ArgumentException("A gacha item needs a permanent Item ID.", nameof(item));

        var localIds = GetLocalUnlockedItemIds();
        if (!localIds.Contains(item.ItemId))
            throw new InvalidOperationException("Cannot equip a locked item.");

        SaveLocal(localIds, item.ItemId, item.CollarColorHex);
        //save marker equip appearance
        EquippedGachaMarkerIcon.Save(item);
        var inventoryRef = db.Document($"users/{uid}/inventory/main");
        var profileRef = db.Document($"leaderboardProfiles/{uid}");

        return db.RunTransactionAsync(transaction =>
        {
            transaction.Set(inventoryRef, new Dictionary<string, object>
            {
                ["equippedItemId"] = item.ItemId,
                ["updatedAt"] = FieldValue.ServerTimestamp
            }, SetOptions.MergeAll);
            transaction.Set(profileRef, new Dictionary<string, object>
            {
                ["equippedItemId"] = item.ItemId,
                ["equippedCollarColor"] = item.CollarColorHex,
                ["updatedAt"] = FieldValue.ServerTimestamp
            }, SetOptions.MergeAll);
            return Task.FromResult(0);
        });
    }

    public async Task SyncFromCloudToLocalAsync(string uid)
    {
        var snapshot = await db.Document($"users/{uid}/inventory/main").GetSnapshotAsync();
        if (!snapshot.Exists) return;

        var unlockedIds = ReadItemIds(snapshot);
        var equippedId = snapshot.TryGetValue("equippedItemId", out string savedId) ? savedId : "";

        // Old documents have only the legacy fields. They remain readable during migration.
        if (string.IsNullOrEmpty(equippedId) && snapshot.TryGetValue("equippedCollarColor", out string oldColor))
            PlayerPrefs.SetString("EquippedCollarColor", oldColor);

        SaveLocal(unlockedIds, equippedId, PlayerPrefs.GetString("EquippedCollarColor", "#ee2b35"));
    }

    private static HashSet<string> ReadItemIds(DocumentSnapshot snapshot)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (snapshot.Exists && snapshot.TryGetValue("unlockedItemIds", out List<object> savedIds))
            foreach (var id in savedIds) ids.Add(id.ToString());

        // Read the former field as well, so existing players keep their unlocks.
        if (snapshot.Exists && snapshot.TryGetValue("unlockedCollars", out List<object> oldIds))
            foreach (var id in oldIds) ids.Add(MigrateLegacyItemId(id.ToString()));
        return ids;
    }

    private static void AddIds(HashSet<string> ids, string stored, char separator)
    {
        foreach (var id in stored.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries))
            ids.Add(MigrateLegacyItemId(id.Trim()));
    }

    private static string MigrateLegacyItemId(string itemId)
    {
        // The original implementation saved display names. Convert its three
        // known values so existing players keep their rewards after upgrading.
        switch (itemId)
        {
            case "Bronze": return "Collar_Bronze";
            case "Silver": return "Collar_Silver";
            case "Gold": return "Collar_Gold";
            default: return itemId;
        }
    }

    private static void SaveLocal(HashSet<string> ids, string equippedId, string colourHex)
    {
        PlayerPrefs.SetString(UnlockedItemIdsKey, string.Join("|", ids));
        PlayerPrefs.SetString(EquippedItemIdKey, equippedId ?? "");
        PlayerPrefs.SetString("EquippedCollarColor", string.IsNullOrWhiteSpace(colourHex) ? "#ee2b35" : colourHex);
        PlayerPrefs.Save();
    }
}
