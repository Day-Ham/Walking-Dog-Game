using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Firestore;

public class CollarInventoryUI : MonoBehaviour
{
    [Header("UI References")]
    public Button bronzeButton;
    public Button silverButton;
    public Button goldButton;
    public Button defaultButton; // To revert to the default red
    
    public TMP_Text statusText;

    private async void OnEnable()
    {
        RefreshInventory();
        await SyncWithCloudAsync();
    }
    
    private async System.Threading.Tasks.Task SyncWithCloudAsync()
    {
        var manager = StepCountAndGpsManager.Instance;
        if (manager != null)
        {
            var bootstrap = manager.GetComponent<FirebaseWalkBootstrap>();
            if (bootstrap != null && bootstrap.Wallet != null && !string.IsNullOrEmpty(bootstrap.Wallet.Owner))
            {
                try {
                    var store = new CollarInventoryStore(FirebaseFirestore.DefaultInstance);
                    await store.SyncFromCloudToLocalAsync(bootstrap.Wallet.Owner);
                    RefreshInventory(); // Refresh buttons based on cloud data
                } catch (System.Exception ex) {
                    Debug.LogWarning("Failed to sync inventory: " + ex);
                }
            }
        }
    }

    public void RefreshInventory()
    {
        string unlocked = PlayerPrefs.GetString("UnlockedCollars", "");

        // Only allow clicking the button if it has been unlocked (pulled in gacha)
        if (bronzeButton != null) bronzeButton.interactable = unlocked.Contains("Bronze");
        if (silverButton != null) silverButton.interactable = unlocked.Contains("Silver");
        if (goldButton != null) goldButton.interactable = unlocked.Contains("Gold");
        
        // Default color is always available
        if (defaultButton != null) defaultButton.interactable = true;

        if (statusText != null)
        {
            statusText.text = "Select a collar to equip it!";
        }
    }

    // Call this from the Bronze button's OnClick event
    public void EquipBronze() => EquipAsync("#CD7F32", "Bronze");

    // Call this from the Silver button's OnClick event
    public void EquipSilver() => EquipAsync("#C0C0C0", "Silver");

    // Call this from the Gold button's OnClick event
    public void EquipGold() => EquipAsync("#FFD700", "Gold");

    // Call this from the Default button's OnClick event
    public void EquipDefault() => EquipAsync("#ee2b35", "Default Red");

    private void EquipAsync(string hexColor, string name)
    {
        // 1. Immediately save locally for instant responsiveness
        PlayerPrefs.SetString("EquippedCollarColor", hexColor);
        PlayerPrefs.Save();
        
        if (statusText != null)
        {
            statusText.text = "Equipped " + name + " collar!";
        }

        // 2. Force map update immediately on the main thread
        var map = FindObjectOfType<OpenFreeMapWebViewMap>();
        if (map != null)
        {
            map.ForceSync();
        }

        // 3. Fire-and-forget the cloud save in the background
        var manager = StepCountAndGpsManager.Instance;
        if (manager != null)
        {
            var bootstrap = manager.GetComponent<FirebaseWalkBootstrap>();
            if (bootstrap != null && bootstrap.Wallet != null && !string.IsNullOrEmpty(bootstrap.Wallet.Owner))
            {
                var store = new CollarInventoryStore(FirebaseFirestore.DefaultInstance);
                _ = store.EquipCollarAsync(bootstrap.Wallet.Owner, hexColor);
            }
        }
    }
}
