# Android Google sign-in and live map repair

## Implementation

- The title scene has a **Sign in with Google** button connected to `LoginUIManager`.
- `FirebaseLoginManager.LoginWithGoogle` requests an ID token through Android
  Credential Manager, exchanges it using Firebase `GoogleAuthProvider`, and uses
  the existing successful-login transition to `StepCounterTestAmar`.
- `GoogleSignInBridge.java` presents Google's native account picker. It supports
  first-time users through `GetSignInWithGoogleOption`. Cancellation, unavailable
  accounts, failures, and a two-minute timeout return to the login UI with retry
  available. Only a successful Firebase exchange enters the game.
- Tokens are kept in memory and never logged or written to disk. The Java bridge
  is preserved for JNI calls in minified builds. Signing out clears Firebase and
  requests Credential Manager session cleanup.
- Email/password login and registration remain available. Google sign-in in the
  Unity editor or a non-Android build shows a clear unsupported-platform message.
- The title scene stores the owner's Web OAuth client ID. If the serialized ID is
  empty, the bridge uses Android's generated `default_web_client_id` resource.
- Credential Manager dependencies are included in Gradle and in an EDM4U
  dependency declaration so resolving Android libraries retains them.

## Firebase project setup

Project: `walky-aa25c`; Android package: `com.TheVeryEvilCompany.WalkingDog`.
The owner supplied this Web client ID on 2026-09-18:

`13373242265-pqgelh38f6haoh1mh0hc7lc1q15f9oj9.apps.googleusercontent.com`

The owner supplied an updated Firebase configuration after registering the debug
signing fingerprint. Its Android OAuth client matches this machine's existing
debug certificate SHA-1:

`33:06:B0:23:83:41:25:CD:16:C6:35:85:93:CD:FD:06:DB:58:5A:6C`

This fingerprint applies to local debug builds only. Release/Play builds require
their own signing certificate fingerprints. Keep Google enabled in Authentication
→ Sign-in method. Download the updated `google-services.json`, replace
`Walking Dog/Assets/Firebase/google-services.json`, and allow Firebase/EDM4U to regenerate
the Android resources. Do not put a Web client secret in the app.

## Live map repair

The leaderboard controller was active at scene startup while its `LeaderBG`
child was hidden. Its `OnEnable` hid the live map even though no leaderboard was
visible. The scene now starts with the controller's root inactive and its content
ready to show. Open and Back toggle the controller root, so closing restores the
walking map. The scene setup utility preserves that wiring.

History, leaderboard, and recovery dialogs each own a map-visibility block.
Repeated opens no longer forget which map to restore, and overlapping dialogs
cannot reveal a map before the last dialog closes. Native map creation is deferred
until canvas layout is ready, queued creation respects disabled/destroyed views,
and attachment/bounds/z-order are refreshed after startup and resume. The map can
show its fallback location without GPS or sign-in; territory ownership still
requires authentication. A missing GPS fix is separate from map visibility.

## Verification

`scripts/test-map-routes.cjs` exercises vector/raster route gaps and territory
rendering. `LiveMapTests` covers no-GPS preview state, repeated/overlapping map
visibility blocks, and the scene's initial leaderboard/map state. The existing
leaderboard integration test checks opening and closing its actual scene buttons.

For an Android validation APK, run Unity with `-buildTarget Android` and
`-executeMethod WalkTrackingValidation.BuildGoogleMapAndroid`. Output:
`Walking Dog/Builds/WalkingDog-google-map.apk`.

Physical-device checks still required: cold start with no GPS fix, acquire GPS
and walk, open/close History and Leaderboards repeatedly, pause/resume the app,
cancel/retry Google login, complete Google login with a registered signing
certificate, and confirm the existing game/cloud features use the resulting user.

References: [Firebase Google authentication](https://firebase.google.com/docs/auth/android/google-signin)
and [Android Credential Manager button flow](https://developer.android.com/identity/sign-in/credential-manager-siwg-implementation).

Validation completed on 2026-09-18: all 67 Unity EditMode tests passed, the Node
map route/territory checks passed, and the Android development APK built
successfully. APK signature verification matched the registered SHA-1 above.
The Google button was rendered and checked for text overflow. Existing merge
conflicts in the Firebase folder/config metadata were resolved while preserving
the first existing GUID, preventing malformed generated configuration metadata.
No physical Android device was connected; live Google login and walking remain
unverified on-device.
