package com.walkingdog.auth;

import android.app.Activity;
import android.os.CancellationSignal;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.ClearCredentialStateRequest;
import androidx.credentials.exceptions.ClearCredentialException;
import androidx.credentials.exceptions.GetCredentialException;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.NoCredentialException;
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;
import org.json.JSONObject;

/** Credential Manager owns the account picker. Unity exchanges the ID token with Firebase. */
@androidx.annotation.Keep
public final class GoogleSignInBridge {
    private static CancellationSignal cancellation;
    private static String result = "";
    private static int generation;

    public static String webClientId(Activity activity) {
        int id = activity.getResources().getIdentifier("default_web_client_id", "string", activity.getPackageName());
        return id == 0 ? "" : activity.getString(id);
    }

    public static synchronized void begin(Activity activity, String clientId) {
        cancel();
        final int request = generation;
        cancellation = new CancellationSignal();
        final CancellationSignal signal = cancellation;
        activity.runOnUiThread(() -> {
            if (signal.isCanceled()) return;
            try {
                GetCredentialRequest options = new GetCredentialRequest.Builder()
                    .addCredentialOption(new GetSignInWithGoogleOption.Builder(clientId).build()).build();
                CredentialManager.create(activity).getCredentialAsync(activity, options, signal,
                    activity::runOnUiThread,
                    new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                        @Override public void onResult(GetCredentialResponse response) {
                            try {
                                Credential credential = response.getCredential();
                                if (!(credential instanceof CustomCredential)
                                    || !GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType())) {
                                    complete(request, "", "Google returned an unsupported credential.");
                                    return;
                                }
                                String token = GoogleIdTokenCredential.createFrom(credential.getData()).getIdToken();
                                complete(request, token, "");
                            } catch (Exception exception) {
                                complete(request, "", "Could not read the Google sign-in response. Please retry.");
                            }
                        }
                        @Override public void onError(GetCredentialException error) {
                            String message = error instanceof GetCredentialCancellationException
                                ? "Google sign-in was canceled."
                                : error instanceof NoCredentialException
                                ? "No Google account is available. Add an account on this device and try again."
                                : "Google sign-in failed. Check your connection and Google Play services, then retry.";
                            complete(request, "", message);
                        }
                    });
            } catch (Exception exception) {
                complete(request, "", "Google sign-in could not start. Check the app's Google configuration.");
            }
        });
    }

    private static synchronized void complete(int request, String token, String error) {
        if (request != generation) return;
        try {
            result = new JSONObject().put("idToken", token).put("error", error).toString();
        } catch (Exception ignored) {
            result = "{\"error\":\"Google sign-in failed. Please retry.\"}";
        }
        cancellation = null;
    }

    // Consumed on Unity's main thread; never log or persist this short-lived token.
    public static synchronized String takeResult() {
        String value = result;
        result = "";
        return value;
    }

    public static synchronized void cancel() {
        generation++;
        if (cancellation != null) cancellation.cancel();
        cancellation = null;
        result = "";
    }

    public static void signOut(Activity activity) {
        cancel();
        activity.runOnUiThread(() -> {
            try {
                CredentialManager.create(activity).clearCredentialStateAsync(new ClearCredentialStateRequest(),
                    null, activity::runOnUiThread, new CredentialManagerCallback<Void, ClearCredentialException>() {
                        @Override public void onResult(Void value) { }
                        @Override public void onError(ClearCredentialException error) { }
                    });
            } catch (Exception ignored) { /* Firebase sign-out still completes. */ }
        });
    }
}
