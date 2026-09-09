using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Firebase.Auth;

public class LoginUIManager : MonoBehaviour
{
    [Header("Login/Register UI")]
    public TMP_InputField emailInputField;
    public TMP_InputField passwordInputField;
    public Button loginButton;
    public Button registerButton;
    public TextMeshProUGUI statusText;

    [Header("Scene Transition")]
    public string gameSceneName = "SampleScene";

    private void Start()
    {
        // Hook up button listeners
        if (loginButton != null) loginButton.onClick.AddListener(OnLoginClicked);
        if (registerButton != null) registerButton.onClick.AddListener(OnRegisterClicked);

        // Subscribe to Firebase Login Manager events
        FirebaseLoginManager.Instance.OnLoginSuccess += HandleLoginSuccess;
        FirebaseLoginManager.Instance.OnLoginFailed += HandleLoginFailed;
        FirebaseLoginManager.Instance.OnRegisterSuccess += HandleRegisterSuccess;
        FirebaseLoginManager.Instance.OnRegisterFailed += HandleRegisterFailed;
    }

    private void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        if (FirebaseLoginManager.Instance != null)
        {
            FirebaseLoginManager.Instance.OnLoginSuccess -= HandleLoginSuccess;
            FirebaseLoginManager.Instance.OnLoginFailed -= HandleLoginFailed;
            FirebaseLoginManager.Instance.OnRegisterSuccess -= HandleRegisterSuccess;
            FirebaseLoginManager.Instance.OnRegisterFailed -= HandleRegisterFailed;
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

        UpdateStatus("Logging in...");
        loginButton.interactable = false;
        registerButton.interactable = false;

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

        UpdateStatus("Registering...");
        loginButton.interactable = false;
        registerButton.interactable = false;

        FirebaseLoginManager.Instance.RegisterUser(email, password);
    }

    private void HandleLoginSuccess(FirebaseUser user)
    {
        UpdateStatus("Login successful!");
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
