using System.Collections.Generic;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CollarInventoryUI : MonoBehaviour
{
    [Header("Runtime inventory")]
    [Tooltip("The Scroll View Content object. Runtime item cards are created here.")]
    public RectTransform content;
    [Tooltip("Prefab with GachaInventoryCardUI attached.")]
    public GachaInventoryCardUI itemCardPrefab;
    [Tooltip("The same GachaItem assets used by GachaManager.")]
    public GachaItem[] itemCatalog;

    [Header("Optional UI")]
    public Button defaultButton;
    public TMP_Text statusText;

    private readonly List<GachaInventoryCardUI> spawnedCards = new List<GachaInventoryCardUI>();

    private async void OnEnable()
    {
        RestoreEquippedMarkerAppearance();
        RefreshInventory(); // Shows local cache immediately while Firebase is loading.
        await SyncWithCloudAsync();
    }

    public void RefreshInventory()
    {
        var unlockedIds = CollarInventoryStore.GetLocalUnlockedItemIds();
        ClearGeneratedCards();

        if (content == null || itemCardPrefab == null)
        {
            Debug.LogWarning("Inventory needs Content and an item-card prefab assigned.", this);
            return;
        }

        // Every catalog asset gets a card. Firebase only decides whether it is locked.
        foreach (var item in itemCatalog ?? System.Array.Empty<GachaItem>())
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ItemId)) continue;
            var card = Instantiate(itemCardPrefab, content);
            card.Bind(item, unlockedIds.Contains(item.ItemId), EquipItem);
            spawnedCards.Add(card);
        }

        if (defaultButton != null) defaultButton.interactable = true;
        if (statusText != null) statusText.text = "Select an unlocked collar to equip it.";
    }

    private async System.Threading.Tasks.Task SyncWithCloudAsync()
    {
        var manager = StepCountAndGpsManager.Instance;
        var bootstrap = manager == null ? null : manager.GetComponent<FirebaseWalkBootstrap>();
        if (bootstrap == null || bootstrap.Wallet == null || string.IsNullOrEmpty(bootstrap.Wallet.Owner)) return;

        try
        {
            await new CollarInventoryStore(FirebaseFirestore.DefaultInstance)
                .SyncFromCloudToLocalAsync(bootstrap.Wallet.Owner);
            RestoreEquippedMarkerAppearance(); // refresh appearance
            RefreshInventory();

            var map = FindFirstObjectByType<OpenFreeMapWebViewMap>();
            if (map != null) map.ForceSync();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("Failed to sync inventory: " + ex);
        }
    }

    private void EquipItem(GachaItem item)
    {
        // Update the cached colour immediately so the map reflects the equipment without waiting for Firebase.
        PlayerPrefs.SetString("EquippedCollarColor", item.CollarColorHex);
        PlayerPrefs.SetString(CollarInventoryStore.EquippedItemIdKey, item.ItemId);
        PlayerPrefs.Save();
        EquippedGachaMarkerIcon.Save(item); // save gchaicon on map

        if (statusText != null) statusText.text = "Equipped " + item.DisplayName + "!";
        var map = FindFirstObjectByType<OpenFreeMapWebViewMap>();
        if (map != null) map.ForceSync();

        var manager = StepCountAndGpsManager.Instance;
        var bootstrap = manager == null ? null : manager.GetComponent<FirebaseWalkBootstrap>();
        if (bootstrap != null && bootstrap.Wallet != null && !string.IsNullOrEmpty(bootstrap.Wallet.Owner))
            _ = new CollarInventoryStore(FirebaseFirestore.DefaultInstance).EquipItemAsync(bootstrap.Wallet.Owner, item);
    }

    public void EquipDefault() // function allows icon to switch to default color upon selection
    {
        PlayerPrefs.SetString("EquippedCollarColor", "#ee2b35");
        PlayerPrefs.SetString(CollarInventoryStore.EquippedItemIdKey, "");
        PlayerPrefs.Save();

        EquippedGachaMarkerIcon.Clear();
        if (statusText != null) statusText.text = "switched to default";

        var map = FindFirstObjectByType<OpenFreeMapWebViewMap>();
        if (map != null) map.ForceSync();
    }

    // Kept for the old scene buttons while the runtime card prefab replaces them.
    public void EquipBronze() => EquipItemById("Collar_Bronze");
    public void EquipSilver() => EquipItemById("Collar_Silver");
    public void EquipGold() => EquipItemById("Collar_Gold");

    public void EquipItemById(string itemId)
    {
        foreach (var item in itemCatalog ?? System.Array.Empty<GachaItem>())
        {
            if (item != null && item.ItemId == itemId)
            {
                EquipItem(item);
                return;
            }
        }
        Debug.LogWarning("No catalog item exists with ID '" + itemId + "'.", this);
    }

    // Firebase stores only the item ID. Resolve it through the local catalog whenever
    // inventory is restored, so a fresh device can recreate the WebView-safe icon.

    private void RestoreEquippedMarkerAppearance()
    {
        var equippedItemId = PlayerPrefs.GetString(CollarInventoryStore.EquippedItemIdKey, "");
        if (string.IsNullOrWhiteSpace(equippedItemId))
        {
            EquippedGachaMarkerIcon.Clear();
            return;
        }

        foreach (var item in itemCatalog ?? System.Array.Empty<GachaItem>())
        {
            if (item == null || item.ItemId != equippedItemId) continue;

            // Keep icon and its fallback colour together, even when a cloud profile
            // created by an older app version did not persist the colour.
            PlayerPrefs.SetString("EquippedCollarColor", item.CollarColorHex);
            PlayerPrefs.Save();
            EquippedGachaMarkerIcon.Save(item);
            return;
        }

        // An item that no longer exists in the catalog must not leave a stale icon
        // on the map; the already-stored collar colour remains the visual fallback.
        EquippedGachaMarkerIcon.Clear();
    }

    private void ClearGeneratedCards()
    {
        foreach (var card in spawnedCards)
            if (card != null) Destroy(card.gameObject);
        spawnedCards.Clear();
    }
}
