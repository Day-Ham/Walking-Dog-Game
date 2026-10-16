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

    // Loads cached inventory and kicks off cloud synchronization when enabled.
    private async void OnEnable()
    {
        RestoreEquippedMarkerAppearance();
        RefreshInventory(); // Shows local cache immediately while Firebase is loading.
        await SyncWithCloudAsync();
    }

    // Instantiates UI cards for each catalog item reflecting unlock and equipped status.
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

    // Synchronizes inventory and equipped state with Firestore cloud data.
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

    // Equips the selected collar, persists choice locally, and syncs to Firestore.
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

    // Equips the default red collar and resets icon customization.
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

    // Equips the legacy bronze tier collar.
    public void EquipBronze() => EquipItemById("Collar_Bronze");
    // Equips the legacy silver tier collar.
    public void EquipSilver() => EquipItemById("Collar_Silver");
    // Equips the legacy gold tier collar.
    public void EquipGold() => EquipItemById("Collar_Gold");

    // Finds a catalog item by ID and equips it.
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

    // Resolves equipped collar ID against catalog and updates map marker assets.
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

    // Cleans up all spawned inventory card game objects.
    private void ClearGeneratedCards()
    {
        foreach (var card in spawnedCards)
            if (card != null) Destroy(card.gameObject);
        spawnedCards.Clear();
    }
}
