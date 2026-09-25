using UnityEngine;

public enum GachaTier
{
    Bronze,
    Silver,
    Gold
}

[CreateAssetMenu(fileName = "GachaItem", menuName = "Walking Dog/Gacha Item")]
public class GachaItem : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string CollarID;
    [SerializeField] private string displayName;

    [Header("Inventory presentation")]
    public GachaTier tier;
    public Sprite Icon_img;
    public string CollarColorHex = "#ee2b35";

    // This ID is what Firebase saves. Never change it after players can own it.
    public string ItemId => CollarID;

    // Falls back to the asset name so old assets still have a readable label.
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    private void OnValidate()
    {
        // An empty ID would make different items indistinguishable in Firebase.
        if (string.IsNullOrWhiteSpace(CollarID))
            CollarID = "item_" + name.Trim().ToLowerInvariant().Replace(" ", "_");
    }
}
