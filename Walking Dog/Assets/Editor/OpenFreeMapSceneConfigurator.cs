using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class OpenFreeMapSceneConfigurator
{
    private const string TargetScenePath = "Assets/Scenes/StepCounterTestAmar.unity";
    private const string MapPanelName = "OpenFreeMap Map Panel";

    [MenuItem("Walking Dog/Setup OpenFreeMap Map")]
    public static void ConfigureStepCounterScene()
    {
        var scene = EditorSceneManager.OpenScene(TargetScenePath);
        var canvas = Object.FindAnyObjectByType<Canvas>();

        if (canvas == null)
        {
            Debug.LogError("Cannot set up OpenFreeMap map because the scene has no Canvas.");
            return;
        }

        var mapPanel = FindChild(canvas.transform, MapPanelName);
        if (mapPanel == null)
        {
            mapPanel = new GameObject(MapPanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            mapPanel.transform.SetParent(canvas.transform, false);
        }

        mapPanel.layer = LayerMask.NameToLayer("UI");
        mapPanel.transform.SetAsFirstSibling();

        var rectTransform = mapPanel.GetComponent<RectTransform>();
        ConfigureMapRect(rectTransform);

        var background = mapPanel.GetComponent<Image>();
        if (background == null)
        {
            background = mapPanel.AddComponent<Image>();
        }

        background.color = new Color(0.91f, 0.94f, 0.95f, 1f);
        background.raycastTarget = false;

        var map = mapPanel.GetComponent<OpenFreeMapWebViewMap>();
        if (map == null)
        {
            map = mapPanel.AddComponent<OpenFreeMapWebViewMap>();
        }

        var serializedMap = new SerializedObject(map);
        serializedMap.FindProperty("mapArea").objectReferenceValue = rectTransform;
        serializedMap.FindProperty("mapStyle").enumValueIndex = 0;
        serializedMap.FindProperty("zoom").intValue = 17;
        serializedMap.FindProperty("followGps").boolValue = true;
        serializedMap.FindProperty("allowMapGestures").boolValue = true;
        serializedMap.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("OpenFreeMap map panel configured in StepCounterTestAmar.unity.");
    }

    private static void ConfigureMapRect(RectTransform rectTransform)
    {
        // Native Android WebView draws above Unity UI, so keep this below the step counter.
        rectTransform.anchorMin = new Vector2(0.06f, 0.05f);
        rectTransform.anchorMax = new Vector2(0.94f, 0.31f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = Vector2.zero;
        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
    }

    private static GameObject FindChild(Transform parent, string childName)
    {
        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == childName)
            {
                return child.gameObject;
            }
        }

        return null;
    }
}
