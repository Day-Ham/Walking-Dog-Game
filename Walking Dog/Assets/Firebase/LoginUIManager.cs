using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Firebase.Auth;
using System.Collections;

public class LoginUIManager : MonoBehaviour
{
    [Header("Login/Register UI")]
    public TMP_InputField emailInputField;
    public TMP_InputField passwordInputField;
    public Button loginButton;
    public Button registerButton;
    public Button googleSignInButton;
    public Toggle rememberMeToggle;
    public TextMeshProUGUI statusText;

    [Header("Scene Transition")]
    public string gameSceneName = "SampleScene";
    public GameObject sceneTransistionAnimator; // Animator for scene transition
    private void Start()
    {
        // Hook up button listeners
        if (loginButton != null) loginButton.onClick.AddListener(OnLoginClicked);
        if (registerButton != null) registerButton.onClick.AddListener(OnRegisterClicked);
        if (googleSignInButton != null) googleSignInButton.onClick.AddListener(OnGoogleSignInClicked);

        // Subscribe to Firebase Login Manager events
        FirebaseLoginManager.Instance.OnLoginSuccess += HandleLoginSuccess;
        FirebaseLoginManager.Instance.OnLoginFailed += HandleLoginFailed;
        FirebaseLoginManager.Instance.OnRegisterSuccess += HandleRegisterSuccess;
        FirebaseLoginManager.Instance.OnRegisterFailed += HandleRegisterFailed;

        // Check if user is already signed in (Remember Me / Auto-login)
        StartCoroutine(CheckAutoLogin());
    }

    private IEnumerator CheckAutoLogin()
    {
        // Wait until Firebase is initialized
        yield return new WaitUntil(() => FirebaseLoginManager.Instance != null && FirebaseLoginManager.Instance.IsFirebaseReady);
        
        // If the user unchecked "Remember Me" last time, sign them out
        if (PlayerPrefs.GetInt("RememberMe", 1) == 0)
        {
            FirebaseLoginManager.Instance.SignOutUser();
        }
        // If a user is already logged in, automatically transition to the game scene
        else if (FirebaseLoginManager.Instance.GetCurrentUser() != null)
        {
            UpdateStatus("Logging in automatically...");
            HandleLoginSuccess(FirebaseLoginManager.Instance.GetCurrentUser());
        }
    }

    private void OnDestroy()
    {
        if (loginButton != null) loginButton.onClick.RemoveListener(OnLoginClicked);
        if (registerButton != null) registerButton.onClick.RemoveListener(OnRegisterClicked);
        if (googleSignInButton != null) googleSignInButton.onClick.RemoveListener(OnGoogleSignInClicked);
        // Unsubscribe from events to prevent memory leaks
        if (FirebaseLoginManager.Instance != null)
        {
            FirebaseLoginManager.Instance.OnLoginSuccess -= HandleLoginSuccess;
            FirebaseLoginManager.Instance.OnLoginFailed -= HandleLoginFailed;
            FirebaseLoginManager.Instance.OnRegisterSuccess -= HandleRegisterSuccess;
            FirebaseLoginManager.Instance.OnRegisterFailed -= HandleRegisterFailed;
        }
    }

    private void SaveRememberMePreference()
    {
        if (rememberMeToggle != null)
        {
            PlayerPrefs.SetInt("RememberMe", rememberMeToggle.isOn ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    private void OnLoginClicked()
    {
        string email = emailInputField.text;
        string password = passwordInputField.text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            UpdateStatus("Please enter both email and password.");
            return;
        }

        SaveRememberMePreference();
        
        UpdateStatus("Logging in...");
        loginButton.interactable = false;
        registerButton.interactable = false;
        if (googleSignInButton != null) googleSignInButton.interactable = false;

        FirebaseLoginManager.Instance.LoginUser(email, password);
    }

    private void OnRegisterClicked()
    {
        string email = emailInputField.text;
        string password = passwordInputField.text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            UpdateStatus("Please enter both email and password.");
            return;
        }

        SaveRememberMePreference();

        UpdateStatus("Registering...");
        loginButton.interactable = false;
        registerButton.interactable = false;
        if (googleSignInButton != null) googleSignInButton.interactable = false;

        FirebaseLoginManager.Instance.RegisterUser(email, password);
    }

    private void OnGoogleSignInClicked()
    {
        SaveRememberMePreference();
        
        UpdateStatus("Opening Google sign-in…");
        loginButton.interactable = false;
        registerButton.interactable = false;
        googleSignInButton.interactable = false;
        FirebaseLoginManager.Instance.LoginWithGoogle();
    }

    //brackey style scene transition
    public void loadNextScene(string sceneName)
    {
        StartCoroutine(loadLevel(sceneName));
    }

    IEnumerator loadLevel(string sceneName)
    {
    sceneTransistionAnimator.GetComponent<Animator>().SetTrigger("EnterScene");
        yield return new WaitForSeconds(1.0f);
        SceneManager.LoadScene(sceneName);

    }


    private void HandleLoginSuccess(FirebaseUser user)
    {
        UpdateStatus("Login successful!");
        ResetButtons();
        
        if (!string.IsNullOrEmpty(gameSceneName))
        {   
        //    SceneManager.LoadScene(gameSceneName);
        loadNextScene(gameSceneName);
        }
        else
        {
            Debug.LogError("Game Scene Name is missing or not set");
        }
    }

    private void HandleLoginFailed(string errorMessage)
    {
        UpdateStatus($"Login Failed: {errorMessage}");
        ResetButtons();
    }

    private void HandleRegisterSuccess(FirebaseUser user)
    {
        UpdateStatus("Registration successful! You are now logged in.");
        ResetButtons();
        
        if (!string.IsNullOrEmpty(gameSceneName))
        {
            SceneManager.LoadScene(gameSceneName);
        }
        else
        {
            Debug.LogError("Game Scene Name is not set in LoginUIManager!");
        }
    }

    private void HandleRegisterFailed(string errorMessage)
    {
        UpdateStatus($"Registration Failed: {errorMessage}");
        ResetButtons();
    }

    private void ResetButtons()
    {
        if (googleSignInButton != null) googleSignInButton.interactable = true;
        if (loginButton != null) loginButton.interactable = true;
        if (registerButton != null) registerButton.interactable = true;
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"Login UI Status: {message}");
    }
}
