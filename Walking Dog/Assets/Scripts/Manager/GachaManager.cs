using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Firebase.Firestore;
using Firebase.Auth;
using WalkingDog.Leaderboards;
using TMPro;

public class GachaManager : MonoBehaviour
{
    public Button gachaButton;
    public TMP_Text resultText;
    public TMP_Text currentPointsText;
    
    [Header("Gacha Settings")]
    public long pullCost = 100;
    
    // Casual rates:
    // Bronze: 70%
    // Silver: 25%
    // Gold: 5%
    public float bronzeRate = 0.70f;
    public float silverRate = 0.25f;
    public float goldRate = 0.05f;

    private bool isPulling = false;

    private void Start()
    {
        if (gachaButton != null)
        {
            gachaButton.onClick.AddListener(OnGachaButtonClicked);
        }
    }

    private void OnDestroy()
    {
        if (gachaButton != null)
        {
            gachaButton.onClick.RemoveListener(OnGachaButtonClicked);
        }
    }

    private void Update()
    {
        if (currentPointsText != null)
        {
            var manager = StepCountAndGpsManager.Instance;
            if (manager != null)
            {
                var bootstrap = manager.GetComponent<FirebaseWalkBootstrap>();
                if (bootstrap != null && bootstrap.Wallet != null)
                {
                    currentPointsText.text = "Points: " + bootstrap.Wallet.DisplayText;
                }
            }
        }
    }

    public async void OnGachaButtonClicked()
    {
        if (isPulling) return;
        
        var manager = StepCountAndGpsManager.Instance;
        if (manager == null)
        {
            SetResultText("Error: Manager not found.");
            return;
        }

        var bootstrap = manager.GetComponent<FirebaseWalkBootstrap>();
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

        if (wallet.Snapshot == null || wallet.Snapshot.Balance < pullCost)
        {
            SetResultText("Not enough points! You need " + pullCost + " points.");
            return;
        }

        isPulling = true;
        SetResultText("Pulling...");
        if (gachaButton != null) gachaButton.interactable = false;

        string receiptId = Guid.NewGuid().ToString("N");
        bool success = false;

        try
        {
            await wallet.SpendPointsAsync(pullCost, receiptId);
            
            // Sync with leaderboard
            try 
            {
                await FirebaseLeaderboardWriter.SynchronizePointsBalanceAsync(FirebaseFirestore.DefaultInstance, wallet.Owner);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Failed to sync leaderboard balance: " + e.Message);
            }

            success = true;
        }
        catch (Exception ex)
        {
            SetResultText("Error: " + ex.Message);
            Debug.LogError("Gacha Pull Error: " + ex);
        }

        if (success)
        {
            await PerformPullAsync(wallet.Owner);
        }

        isPulling = false;
        if (gachaButton != null) gachaButton.interactable = true;
    }

    private async Task PerformPullAsync(string uid)
    {
        float roll = UnityEngine.Random.value; // Returns 0.0 to 1.0

        string pulledCollar = "";
        string pulledColor = "";

        if (roll <= goldRate)
        {
            SetResultText("Gold Collar! (Super Rare)");
            pulledCollar = "Gold";
            pulledColor = "#FFD700";
        }
        else if (roll <= goldRate + silverRate)
        {
            SetResultText("Silver Collar! (Rare)");
            pulledCollar = "Silver";
            pulledColor = "#C0C0C0";
        }
        else
        {
            SetResultText("Bronze Collar! (Common)");
            pulledCollar = "Bronze";
            pulledColor = "#CD7F32";
        }
        
        try 
        {
            var store = new CollarInventoryStore(FirebaseFirestore.DefaultInstance);
            await store.UnlockCollarAsync(uid, pulledCollar, pulledColor);
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to save collar to cloud: " + ex);
        }
        
        // Let the map manager know to force an update if the map is open
        var map = FindObjectOfType<OpenFreeMapWebViewMap>();
        if (map != null)
        {
            map.ForceSync();
        }
    }

    private void SetResultText(string message)
    {
        if (resultText != null)
        {
            resultText.text = message;
        }
        Debug.Log("Gacha: " + message);
    }
}
