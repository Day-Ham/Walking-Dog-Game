using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>References on the item-card prefab generated inside the inventory Content object.</summary>
public class GachaInventoryCardUI : MonoBehaviour
{
    public Button button;
    public Image icon;
    public TMP_Text itemNameText;
    public TMP_Text tierText;
    public GameObject lockedOverlay;

    public void Bind(GachaItem item, bool unlocked, Action<GachaItem> onSelected)
    {
        // All visual data comes from the catalog asset, never from Firebase.
        if (icon != null) icon.sprite = item.Icon_img;
        if (itemNameText != null) itemNameText.text = item.DisplayName;
        if (tierText != null) tierText.text = item.tier.ToString();
        if (lockedOverlay != null) lockedOverlay.SetActive(!unlocked);

        if (button == null) return;
        button.interactable = unlocked;
        button.onClick.RemoveAllListeners();
        if (unlocked) button.onClick.AddListener(() => onSelected(item));
    }
}
