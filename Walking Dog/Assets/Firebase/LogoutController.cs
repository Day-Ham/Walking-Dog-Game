using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Handles the confirmation action in the in-game logout dialog.
///
/// A manual logout is intentionally different from simply leaving the game scene:
/// it clears the Firebase/Google session and disables Remember Me so a later launch
/// remains on the title screen until the player signs in again.
/// </summary>
[RequireComponent(typeof(Button))]
public sealed class LogoutController : MonoBehaviour
{
    [SerializeField] private string titleSceneName = "Title Screen";

    private Button logoutButton;
    private bool isLoggingOut;



    public void LogOutAndReturnToTitle()
    {
        if (isLoggingOut) return; // Ignore rapid double-clicks while the scene changes.
        isLoggingOut = true;
       

        // Persist this first. Even if Firebase initialization is unavailable, an
        // explicit logout must not be undone later by Remember Me auto-login.
        PlayerPrefs.SetInt("RememberMe", 0);
        PlayerPrefs.Save();

        var loginManager = FirebaseLoginManager.Instance;
        if (loginManager != null)
        {
            loginManager.SignOutUser();
        }
        else
        {
            Debug.LogWarning("Logout continued without FirebaseLoginManager; the saved auto-login preference was still cleared.");
        }

        if (string.IsNullOrWhiteSpace(titleSceneName))
        {
            Debug.LogError("LogoutController needs a title scene name.");
            isLoggingOut = false;
            logoutButton.interactable = true;
            return;
        }

        SceneManager.LoadScene(titleSceneName);
    }
}
