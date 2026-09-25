using System;
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

/// <summary>
/// Converts the equipped item's Sprite into a WebView-safe PNG data URL.
/// The conversion happens only when equipment changes, never on each GPS update.
/// </summary>
public static class EquippedGachaMarkerIcon
{
    public const string PlayerPrefsKey = "EquippedGachaMarkerIconDataUrl";
    private const int MaximumIconPixels = 128;

    public static void Save(GachaItem item)
    {
        PlayerPrefs.SetString(PlayerPrefsKey, item == null ? "" : EncodeSprite(item.Icon_img));
        PlayerPrefs.Save();
    }

    public static void Clear()
    {
        PlayerPrefs.DeleteKey(PlayerPrefsKey);
        PlayerPrefs.Save();
    }

    private static string EncodeSprite(Sprite sprite)
    {
        if (sprite == null || sprite.texture == null) return "";

        // Blitting to a RenderTexture works even when the source texture's Read/Write
        // setting is disabled. This also crops correctly when the icon is in an atlas.
        var source = sprite.texture;
        var sourceRect = sprite.textureRect;
        var width = Mathf.Clamp(Mathf.CeilToInt(sourceRect.width), 1, MaximumIconPixels);
        var height = Mathf.Clamp(Mathf.CeilToInt(sourceRect.height), 1, MaximumIconPixels);
        var scale = new Vector2(sourceRect.width / source.width, sourceRect.height / source.height);
        var offset = new Vector2(sourceRect.x / source.width, sourceRect.y / source.height);
        var renderTarget = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        Texture2D readableCopy = null;

        try
        {
            Graphics.Blit(source, renderTarget, scale, offset);
            RenderTexture.active = renderTarget;
            readableCopy = new Texture2D(width, height, TextureFormat.RGBA32, false);
            readableCopy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readableCopy.Apply(false, false);
            return "data:image/png;base64," + Convert.ToBase64String(readableCopy.EncodeToPNG());
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not create GPS marker icon: " + exception.Message);
            return "";
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTarget);
            if (readableCopy != null) UnityEngine.Object.Destroy(readableCopy);
        }
    }
}
