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
            RefreshInventory();
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

        if (statusText != null) statusText.text = "Equipped " + item.DisplayName + "!";
        var map = FindFirstObjectByType<OpenFreeMapWebViewMap>();
        if (map != null) map.ForceSync();

        var manager = StepCountAndGpsManager.Instance;
        var bootstrap = manager == null ? null : manager.GetComponent<FirebaseWalkBootstrap>();
        if (bootstrap != null && bootstrap.Wallet != null && !string.IsNullOrEmpty(bootstrap.Wallet.Owner))
            _ = new CollarInventoryStore(FirebaseFirestore.DefaultInstance).EquipItemAsync(bootstrap.Wallet.Owner, item);
    }

    public void EquipDefault()
    {
        PlayerPrefs.SetString("EquippedCollarColor", "#ee2b35");
        PlayerPrefs.SetString(CollarInventoryStore.EquippedItemIdKey, "");
        PlayerPrefs.Save();
        if (statusText != null) statusText.text = "Equipped Default Red collar!";
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

    private void ClearGeneratedCards()
    {
        foreach (var card in spawnedCards)
            if (card != null) Destroy(card.gameObject);
        spawnedCards.Clear();
    }
}
