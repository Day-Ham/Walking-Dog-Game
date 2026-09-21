using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class AuthenticationSceneSetup
{
    [MenuItem("Walking Dog/Connect Google Sign In")]
    public static void Configure()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Title Screen.unity");
        var ui = Object.FindAnyObjectByType<LoginUIManager>(FindObjectsInactive.Include);
        var parent = ui.registerButton.transform.parent;
        var existing = parent.Find("Google Sign In");
        var button = existing != null ? existing.GetComponent<Button>() : Object.Instantiate(ui.registerButton, parent);
        button.name = "Google Sign In";
        button.onClick = new Button.ButtonClickedEvent();
        var rect = (RectTransform)button.transform;
        var register = (RectTransform)ui.registerButton.transform;
        rect.anchoredPosition = register.anchoredPosition + new Vector2(0, -160);
        button.image.color = Color.white;
        button.colors = ColorBlock.defaultColorBlock;
        var text = button.GetComponentInChildren<TMP_Text>(true);
        text.text = "Sign in with Google";
        text.fontStyle = FontStyles.Normal;
        text.fontSize = 9;
        text.enableAutoSizing = false;
        text.color = new Color(0.12f, 0.12f, 0.12f);
        ui.googleSignInButton = button;
        // Move the existing Back control below the added authentication option.
        foreach (var candidate in parent.GetComponentsInChildren<Button>(true))
        {
            if (candidate == button || candidate == ui.loginButton || candidate == ui.registerButton) continue;
            var label = candidate.GetComponentInChildren<TMP_Text>(true);
            if (label != null && (label.text.Trim().ToUpperInvariant() == "BACK" || label.text.Trim().ToUpperInvariant() == "CANCEL"))
                ((RectTransform)candidate.transform).anchoredPosition = rect.anchoredPosition + new Vector2(0, -160);
        }
        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Google sign-in button connected to the title screen.");
    }

    public static void ConfigureAndCapture()
    {
        Configure();
        var ui = Object.FindAnyObjectByType<LoginUIManager>(FindObjectsInactive.Include);
        for (var parent = ui.googleSignInButton.transform.parent; parent != null; parent = parent.parent)
            parent.gameObject.SetActive(true);
        ui.sceneTransistionAnimator.SetActive(false);
        // Reuse the project's screenshot utility; preview changes are not saved.
        var capture = typeof(WalkTrackingValidation).GetMethod("Capture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        capture.Invoke(null, new object[] { ui.GetComponent<Canvas>(), "Logs/google-sign-in-preview.png", 946, 2048 });
        var googleLabel = ui.googleSignInButton.GetComponentInChildren<TMP_Text>();
        googleLabel.ForceMeshUpdate();
        if (googleLabel.isTextOverflowing) throw new System.Exception("Google sign-in label overflows the button.");
    }

    public static void ValidateAndBuild()
    {
        ConfigureAndCapture();
        // Build only the saved scenes, not the temporary screenshot layout.
        EditorSceneManager.OpenScene("Assets/Scenes/Title Screen.unity");
        WalkTrackingValidation.BuildGoogleMapAndroid();
    }
}
