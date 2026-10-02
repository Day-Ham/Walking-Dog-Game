using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WalkingDog.Leaderboards;

public class GachaManager : MonoBehaviour
{
    public Button gachaButton;
    public TMP_Text resultText;
    public TMP_Text currentPointsText;

    public Button milestoneButton;

    [Header("Gacha catalog")]
    [Tooltip("Add every GachaItem asset here. Items are selected from this catalog.")]
    public GachaItem[] itemCatalog;

    [Header("Gacha settings")]
    public long pullCost = 100;
    [Range(0f, 1f)] public float bronzeRate = 0.70f;
    [Range(0f, 1f)] public float silverRate = 0.25f;
    [Range(0f, 1f)] public float goldRate = 0.05f;

    private bool isPulling;

    private void Start()
    {
        if (gachaButton != null)
            gachaButton.onClick.AddListener(OnGachaButtonClicked);
        if (milestoneButton != null)
            milestoneButton.onClick.AddListener(OnMilestoneButtonClicked);
    }

    private void OnDestroy()
    {
        if (gachaButton != null)
            gachaButton.onClick.RemoveListener(OnGachaButtonClicked);
        if (milestoneButton != null)
            milestoneButton.onClick.RemoveListener(OnMilestoneButtonClicked);
    }

    private void Update()
    {
        var manager = StepCountAndGpsManager.Instance;
        var bootstrap = manager == null ? null : manager.GetComponent<FirebaseWalkBootstrap>();
        if (currentPointsText != null && bootstrap != null && bootstrap.Wallet != null)
            currentPointsText.text = "Points: " + bootstrap.Wallet.DisplayText;

        if (milestoneButton != null)
        {
            if (bootstrap != null && bootstrap.Wallet != null && bootstrap.Wallet.Snapshot != null)
            {
                long totalSteps = bootstrap.Wallet.Snapshot.TotalEarned * 10;
                long totalFreeRolls = totalSteps / 10000;
                long claimed = bootstrap.Wallet.Snapshot.MilestoneRollsClaimed;
                milestoneButton.gameObject.SetActive(totalFreeRolls > claimed);
            }
            else
            {
                milestoneButton.gameObject.SetActive(false);
            }
        }
    }

    public async void OnGachaButtonClicked()
    {
        if (isPulling) return;

        var manager = StepCountAndGpsManager.Instance;
        var bootstrap = manager == null ? null : manager.GetComponent<FirebaseWalkBootstrap>();
        if (bootstrap == null || !bootstrap.IsReady || bootstrap.Wallet == null)
        {
            SetResultText("Error: Wallet not ready.");
            return;
        }

        var wallet = bootstrap.Wallet;
        wallet.SynchronizeAccount();
        if (string.IsNullOrEmpty(wallet.Owner))
        {
            SetResultText("Please sign in first.");
            return;
        }

        var item = RollItem();
        if (item == null)
        {
            SetResultText("No valid gacha items are configured.");
            return;
        }

        if (wallet.Snapshot == null || wallet.Snapshot.Balance < pullCost)
        {
            SetResultText("Not enough points! You need " + pullCost + " points.");
            return;
        }

        isPulling = true;
        SetResultText("Pulling...");
        if (gachaButton != null) gachaButton.interactable = false;
        if (milestoneButton != null) milestoneButton.interactable = false;

        try
        {
            await wallet.SpendPointsAsync(pullCost, Guid.NewGuid().ToString("N"));
            await FirebaseLeaderboardWriter.SynchronizePointsBalanceAsync(FirebaseFirestore.DefaultInstance, wallet.Owner);
            await GrantRolledItemAsync(wallet.Owner, item);
        }
        catch (Exception ex)
        {
            SetResultText("Pull failed: " + ex.Message);
            Debug.LogError("Gacha Pull Error: " + ex);
        }
        finally
        {
            isPulling = false;
            if (gachaButton != null) gachaButton.interactable = true;
            if (milestoneButton != null) milestoneButton.interactable = true;
        }
    }

    public async void OnMilestoneButtonClicked()
    {
        if (isPulling) return;

        var manager = StepCountAndGpsManager.Instance;
        var bootstrap = manager == null ? null : manager.GetComponent<FirebaseWalkBootstrap>();
        if (bootstrap == null || !bootstrap.IsReady || bootstrap.Wallet == null)
        {
            SetResultText("Error: Wallet not ready.");
            return;
        }

        var wallet = bootstrap.Wallet;
        wallet.SynchronizeAccount();
        if (string.IsNullOrEmpty(wallet.Owner))
        {
            SetResultText("Please sign in first.");
            return;
        }

        var item = RollItem();
        if (item == null)
        {
            SetResultText("No valid gacha items are configured.");
            return;
        }

        if (wallet.Snapshot == null)
        {
            SetResultText("Wallet snapshot not loaded.");
            return;
        }
        
        long totalSteps = wallet.Snapshot.TotalEarned * 10;
        long totalFreeRolls = totalSteps / 10000;
        if (totalFreeRolls <= wallet.Snapshot.MilestoneRollsClaimed)
        {
            SetResultText("No milestone rolls available.");
            return;
        }

        isPulling = true;
        SetResultText("Pulling free milestone roll...");
        if (gachaButton != null) gachaButton.interactable = false;
        if (milestoneButton != null) milestoneButton.interactable = false;

        try
        {
            await wallet.ClaimMilestoneRollAsync(Guid.NewGuid().ToString("N"));
            await GrantRolledItemAsync(wallet.Owner, item);
        }
        catch (Exception ex)
        {
            SetResultText("Milestone pull failed: " + ex.Message);
            Debug.LogError("Milestone Pull Error: " + ex);
        }
        finally
        {
            isPulling = false;
            if (gachaButton != null) gachaButton.interactable = true;
            if (milestoneButton != null) milestoneButton.interactable = true;
        }
    }

    private GachaItem RollItem()
    {
        // First choose the rarity tier. Multiple items in a tier share that tier's rate equally.
        var roll = UnityEngine.Random.value;
        var rolledTier = roll < goldRate ? GachaTier.Gold :
            roll < goldRate + silverRate ? GachaTier.Silver : GachaTier.Bronze;

        var candidates = new List<GachaItem>();
        foreach (var item in itemCatalog ?? Array.Empty<GachaItem>())
        {
            if (item != null && item.tier == rolledTier && !string.IsNullOrWhiteSpace(item.ItemId))
                candidates.Add(item);
        }

        if (candidates.Count == 0)
        {
            Debug.LogError("Gacha catalog has no " + rolledTier + " items.");
            return null;
        }

        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    private async Task GrantRolledItemAsync(string uid, GachaItem item)
    {
        // Firebase receives only item.ItemId. Icon, name, tier, and colour stay in the local asset.
        var store = new CollarInventoryStore(FirebaseFirestore.DefaultInstance);
        await store.UnlockItemAsync(uid, item);
        SetResultText(item.DisplayName + " unlocked! (" + item.tier + ")");

        var map = FindFirstObjectByType<OpenFreeMapWebViewMap>();
        if (map != null) map.ForceSync();
    }

    private void SetResultText(string message)
    {
        if (resultText != null) resultText.text = message;
        Debug.Log("Gacha: " + message);
    }
}
