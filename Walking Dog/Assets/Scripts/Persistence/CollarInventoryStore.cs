using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

public class CollarInventoryStore
{
    private readonly FirebaseFirestore db;

    public CollarInventoryStore(FirebaseFirestore db)
    {
        this.db = db;
    }

    public async Task UnlockCollarAsync(string uid, string collarId, string colorHex)
    {
        // Save to Local as Backup
        string unlocked = PlayerPrefs.GetString("UnlockedCollars", "");
        if (!unlocked.Contains(collarId))
        {
            unlocked += (string.IsNullOrEmpty(unlocked) ? "" : ",") + collarId;
            PlayerPrefs.SetString("UnlockedCollars", unlocked);
        }
        PlayerPrefs.SetString("EquippedCollarColor", colorHex);
        PlayerPrefs.Save();

        // Save to Firebase Cloud (Inventory)
        var inventoryRef = db.Document($"users/{uid}/inventory/main");
        
        // Save to Public Profile so others can see it (if we want to render it on leaderboards later)
        var profileRef = db.Document($"leaderboardProfiles/{uid}");

        await db.RunTransactionAsync(async transaction =>
        {
            var invSnapshot = await transaction.GetSnapshotAsync(inventoryRef);
            
            List<string> unlockedCollars = new List<string>();
            if (invSnapshot.Exists && invSnapshot.TryGetValue("unlockedCollars", out List<object> existingList))
            {
                foreach (var item in existingList) unlockedCollars.Add(item.ToString());
            }

            if (!unlockedCollars.Contains(collarId))
            {
                unlockedCollars.Add(collarId);
            }

            transaction.Set(inventoryRef, new Dictionary<string, object>
            {
                ["unlockedCollars"] = unlockedCollars,
                ["equippedCollarColor"] = colorHex,
                ["updatedAt"] = FieldValue.ServerTimestamp
            }, SetOptions.MergeAll);

            transaction.Set(profileRef, new Dictionary<string, object>
            {
                ["equippedCollarColor"] = colorHex,
                ["updatedAt"] = FieldValue.ServerTimestamp
            }, SetOptions.MergeAll);
        });
    }

    public async Task EquipCollarAsync(string uid, string colorHex)
    {
        // Local Backup
        PlayerPrefs.SetString("EquippedCollarColor", colorHex);
        PlayerPrefs.Save();

        var inventoryRef = db.Document($"users/{uid}/inventory/main");
        var profileRef = db.Document($"leaderboardProfiles/{uid}");

        await db.RunTransactionAsync(async transaction =>
        {
            transaction.Set(inventoryRef, new Dictionary<string, object>
            {
                ["equippedCollarColor"] = colorHex,
                ["updatedAt"] = FieldValue.ServerTimestamp
            }, SetOptions.MergeAll);

            transaction.Set(profileRef, new Dictionary<string, object>
            {
                ["equippedCollarColor"] = colorHex,
                ["updatedAt"] = FieldValue.ServerTimestamp
            }, SetOptions.MergeAll);
        });
    }

    public async Task SyncFromCloudToLocalAsync(string uid)
    {
        var inventoryRef = db.Document($"users/{uid}/inventory/main");
        var snapshot = await inventoryRef.GetSnapshotAsync();

        if (snapshot.Exists)
        {
            if (snapshot.TryGetValue("unlockedCollars", out List<object> cloudUnlocked))
            {
                List<string> strList = new List<string>();
                foreach (var item in cloudUnlocked) strList.Add(item.ToString());
                PlayerPrefs.SetString("UnlockedCollars", string.Join(",", strList));
            }

            if (snapshot.TryGetValue("equippedCollarColor", out string cloudEquipped))
            {
                PlayerPrefs.SetString("EquippedCollarColor", cloudEquipped);
            }
            PlayerPrefs.Save();
        }
    }
}
