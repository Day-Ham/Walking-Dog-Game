using System;
using System.Collections;
using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;

public class FirebaseLoginManager : MonoBehaviour
{
    public static FirebaseLoginManager Instance { get; private set; }

    private FirebaseAuth auth;
    private FirebaseUser user;

    public bool IsFirebaseReady { get; private set; }

    public event Action<FirebaseUser> OnLoginSuccess;
    public event Action<string> OnLoginFailed;
    public event Action<FirebaseUser> OnRegisterSuccess;
    public event Action<string> OnRegisterFailed;
    public event Action OnSignOut;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        InitializeFirebase();
    }

    private void InitializeFirebase()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available)
            {
                InitializeAuth();
                IsFirebaseReady = true;
                Debug.Log("Firebase is ready to use.");
            }
            else
            {
                Debug.LogError($"Could not resolve all Firebase dependencies: {dependencyStatus}");
            }
        });
    }

    private void InitializeAuth()
    {
        auth = FirebaseAuth.DefaultInstance;
        auth.StateChanged += AuthStateChanged;
        AuthStateChanged(this, null);
    }

    private void AuthStateChanged(object sender, EventArgs eventArgs)
    {
        if (auth.CurrentUser != user)
        {
            bool signedIn = user != auth.CurrentUser && auth.CurrentUser != null;
            if (!signedIn && user != null)
            {
                Debug.Log("Signed out " + user.UserId);
                OnSignOut?.Invoke();
            }
            user = auth.CurrentUser;
            if (signedIn)
            {
                Debug.Log("Signed in " + user.UserId);
            }
        }
    }

    public void LoginUser(string email, string password)
    {
        if (!IsFirebaseReady)
        {
            OnLoginFailed?.Invoke("Firebase is not ready yet.");
            return;
        }

        auth.SignInWithEmailAndPasswordAsync(email, password).ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled)
            {
                Debug.LogError("SignInWithEmailAndPasswordAsync was canceled.");
                OnLoginFailed?.Invoke("Login was canceled.");
                return;
            }
            if (task.IsFaulted)
            {
                Debug.LogError("SignInWithEmailAndPasswordAsync encountered an error: " + task.Exception);
                OnLoginFailed?.Invoke(GetErrorMessage(task.Exception));
                return;
            }

            AuthResult result = task.Result;
            Debug.LogFormat("User signed in successfully: {0} ({1})", result.User.DisplayName, result.User.UserId);
            OnLoginSuccess?.Invoke(result.User);
        });
    }

    public void RegisterUser(string email, string password)
    {
        if (!IsFirebaseReady)
        {
            OnRegisterFailed?.Invoke("Firebase is not ready yet.");
            return;
        }

        auth.CreateUserWithEmailAndPasswordAsync(email, password).ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled)
            {
                Debug.LogError("CreateUserWithEmailAndPasswordAsync was canceled.");
                OnRegisterFailed?.Invoke("Registration was canceled.");
                return;
            }
            if (task.IsFaulted)
            {
                Debug.LogError("CreateUserWithEmailAndPasswordAsync encountered an error: " + task.Exception);
                OnRegisterFailed?.Invoke(GetErrorMessage(task.Exception));
                return;
            }

            AuthResult result = task.Result;
            Debug.LogFormat("Firebase user created successfully: {0} ({1})", result.User.DisplayName, result.User.UserId);
            OnRegisterSuccess?.Invoke(result.User);
        });
    }

    public void SignOutUser()
    {
        if (auth != null && auth.CurrentUser != null)
        {
            auth.SignOut();
        }
    }

    public FirebaseUser GetCurrentUser()
    {
        return user;
    }

    private string GetErrorMessage(AggregateException exception)
    {
        FirebaseException firebaseEx = exception.GetBaseException() as FirebaseException;
        if (firebaseEx != null)
        {
            AuthError errorCode = (AuthError)firebaseEx.ErrorCode;
            switch (errorCode)
            {
                case AuthError.MissingEmail:
                    return "Missing Email";
                case AuthError.MissingPassword:
                    return "Missing Password";
                case AuthError.WrongPassword:
                    return "Invalid Password";
                case AuthError.InvalidEmail:
                    return "Invalid Email";
                case AuthError.UserNotFound:
                    return "User Not Found";
                case AuthError.EmailAlreadyInUse:
                    return "Email Already In Use";
                case AuthError.WeakPassword:
                    return "Weak Password";
                default:
                    return "An error occurred: " + errorCode.ToString();
            }
        }
        return "An unknown error occurred.";
    }
}
