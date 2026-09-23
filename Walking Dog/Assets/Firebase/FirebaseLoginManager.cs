using System;
using System.Collections;
using System.Threading;
using UnityEngine;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;

public class FirebaseLoginManager : MonoBehaviour
{
    public static FirebaseLoginManager Instance { get; private set; }

    private FirebaseAuth auth;
    private FirebaseUser user;
    [Tooltip("Optional Web OAuth client ID. Normally read from the updated google-services.json Android resources.")]
    [SerializeField] private string googleWebClientId = "";
    private CancellationTokenSource googleLogin;
    public bool IsGoogleLoginInProgress => googleLogin != null;

    public bool IsFirebaseReady { get; private set; }
    public string InitializationError { get; private set; }

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
            if (this == null || Instance != this) return;
            if (task.IsCanceled || task.IsFaulted)
            {
                InitializationError = "Firebase could not start. Please restart the app and try again.";
                Debug.LogWarning(InitializationError);
                return;
            }

            var dependencyStatus = task.Result;
            if (dependencyStatus == DependencyStatus.Available)
            {
                InitializeAuth();
                IsFirebaseReady = true;
                Debug.Log("Firebase is ready to use.");
            }
            else
            {
                InitializationError = "Firebase dependencies are unavailable: " + dependencyStatus;
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
        if (IsGoogleLoginInProgress) return;
        if (!IsFirebaseReady)
        {
            OnLoginFailed?.Invoke(InitializationError ?? "Firebase is not ready yet.");
            return;
        }

        auth.SignInWithEmailAndPasswordAsync(email, password).ContinueWithOnMainThread(task =>
        {
            if (this == null || Instance != this) return;
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
        if (IsGoogleLoginInProgress) return;
        if (!IsFirebaseReady)
        {
            OnRegisterFailed?.Invoke(InitializationError ?? "Firebase is not ready yet.");
            return;
        }

        auth.CreateUserWithEmailAndPasswordAsync(email, password).ContinueWithOnMainThread(task =>
        {
            if (this == null || Instance != this) return;
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

    public async void LoginWithGoogle()
    {
        if (IsGoogleLoginInProgress) return;
        if (!IsFirebaseReady)
        {
            OnLoginFailed?.Invoke(InitializationError ?? "Firebase is not ready yet.");
            return;
        }
        var request = new CancellationTokenSource();
        googleLogin = request;
        try
        {
            string token = await AndroidGoogleSignIn.GetIdTokenAsync(googleWebClientId, request.Token);
            request.Token.ThrowIfCancellationRequested();
            using (var credential = GoogleAuthProvider.GetCredential(token, null))
            {
                var signedInUser = await auth.SignInWithCredentialAsync(credential);
                if (this == null || Instance != this || request.IsCancellationRequested) return;
                OnLoginSuccess?.Invoke(signedInUser);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (this != null && Instance == this && !request.IsCancellationRequested)
            {
                var message = exception is InvalidOperationException ? exception.Message
                    : "Google authentication failed. Please retry or use email/password.";
                OnLoginFailed?.Invoke(message);
            }
        }
        finally
        {
            if (ReferenceEquals(googleLogin, request)) googleLogin = null;
            request.Dispose();
        }
    }

    public void SignOutUser() // logout sign out for google 
    {
        // An explicit sign-out must win over an in-progress Google sign-in. Cancelling
        // the request prevents its continuation from completing a Firebase login after
        // the user has already been returned to the title screen.
        googleLogin?.Cancel();
        AndroidGoogleSignIn.SignOut();
        if (auth != null && auth.CurrentUser != null)
        {
            auth.SignOut();
        }
    }

    public FirebaseUser GetCurrentUser()
    {
        return user;
    }

    private void OnDestroy()
    {
        googleLogin?.Cancel();
        if (auth != null) auth.StateChanged -= AuthStateChanged;
        IsFirebaseReady = false;
        if (Instance == this) Instance = null;
        user = null;
        auth = null; // Firebase's default instance is shared; do not dispose it here.
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
