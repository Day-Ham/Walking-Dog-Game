using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

internal static class CreateWalkHistoryEntryPrefab
{
    [MenuItem("Tools/Walking Dog/Create Walk History Entry Prefab")]
    // Creates the editable prefab from the exact runtime layout formerly
    // assembled in WalkHistoryUI.Render(). Safe to run again to replace it.
    public static void Create()
    {
        const string folder = "Assets/prefab/walks";
        const string path = folder + "/WalkHistoryEntry.prefab";
        if (!AssetDatabase.IsValidFolder("Assets/prefab/walks"))
            AssetDatabase.CreateFolder("Assets/prefab", "walks");

        var root = new GameObject("Walk History Entry", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(WalkDataTab));
        var image = root.GetComponent<Image>();
        image.color = new Color(0.13f, 0.20f, 0.24f);
        var button = root.GetComponent<Button>();
        button.targetGraphic = image;
        var layout = root.GetComponent<LayoutElement>();
        layout.minHeight = 116;
        layout.preferredHeight = 116;

        var date = CreateLabel("Date", root.transform, "Oct 9, 2026  •  12:00 PM", 26, Color.white);
        Place(date.rectTransform, new Vector2(0, 0.5f), Vector2.one, new Vector2(20, 0), new Vector2(-20, -10));
        var measurements = CreateLabel("Measurements", root.transform, "1,234 steps   •   1.23 km   •   20 min 0 sec", 23, new Color(0.72f, 0.84f, 0.87f));
        Place(measurements.rectTransform, Vector2.zero, new Vector2(1, 0.5f), new Vector2(20, 10), new Vector2(-20, 0));

        var serializedTab = new SerializedObject(root.GetComponent<WalkDataTab>());
        serializedTab.FindProperty("dateText").objectReferenceValue = date;
        serializedTab.FindProperty("measurementsText").objectReferenceValue = measurements;
        serializedTab.FindProperty("selectButton").objectReferenceValue = button;
        serializedTab.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static TMP_Text CreateLabel(string name, Transform parent, string value, float size, Color color)
    {
        var label = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(parent, false);
        label.text = value;
        label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = size;
        label.enableAutoSizing = true;
        label.fontSizeMin = size * 0.75f;
        label.fontSizeMax = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        label.richText = false;
        return label;
    }

    private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 insetMin, Vector2 insetMax)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = insetMin;
        rect.offsetMax = insetMax;
    }
}
