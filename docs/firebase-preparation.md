# Local saves and Firebase integration

Firebase Auth and Firestore SDK 13.16.0 and the Android configuration for
`walky-aa25c` are present. The Android package matches Unity's
`com.TheVeryEvilCompany.WalkingDog`. The adapter and initialization component
are implemented. `FirebaseWalkBootstrap` is now attached to `StepCountManager`
in `StepCounterTestAmar`, so completed walks owned by the signed-in player are
eligible for upload after initialization. Email/password login and registration are implemented in
`Assets/Firebase/FirebaseLoginManager.cs` and `LoginUIManager.cs`. The title scene
has both managers and its UI references assigned, and loads `StepCounterTestAmar`
after successful authentication. Google sign-in is enabled in the console but
has no Unity implementation yet. Live login/upload testing remains pending.

## Current behavior

- Ending a walk writes its full record, including GPS samples, to
  `Application.persistentDataPath/WalkSessions/walk_{fullWalkId}.json`.
- Completed walks are immutable. Repeating a save uses the same ID/file and
  preserves its existing upload state.
- Writes are flushed to a temporary file before replacement. Replacing an
  existing healthy file retains a `.bak` copy. Loads can recover from that backup;
  unfinished `.tmp` files are ignored. A backup can be one state change behind,
  which is why uploads must be safe to retry.
- Existing timestamp-named JSON saves remain readable. Missing schema versions
  are interpreted as the original version and normalized in memory. History is
  deduplicated by walk ID; a canonical file takes priority. Original legacy files
  are retained, including when ownership or sync state gets a canonical file.
- Invalid records are skipped with a warning rather than deleted. Unsupported
  versions cannot be treated as current records.
- A walk's queue metadata lives in the same file as its data. `Pending` means
  saved locally; `RetryNeeded` means an upload was attempted but is not yet
  acknowledged; `Synced` means the adapter returned server acknowledgement.
- The current UI reports **Saved on device**. With a real adapter attached, it can
  report **Saved on device - sync pending** or **Summary synced to cloud**.

## Code responsibilities

| Code | Responsibility |
| --- | --- |
| `StepCountAndGpsManager` | Tracking, creation of completed records, account capture at walk start, and scheduling sync |
| `LocalWalkRepository` | Validation, local saves, recovery, history, ownership, durable queue state |
| `WalkSyncService` | Upload selection, account isolation, retries, backoff, cancellation, acknowledgement |
| `IWalkCloudStore` | Adapter contract for authenticated cloud uploads |
| `WalkSummary` | Explicit cloud payload, excluding all GPS coordinates and route samples |
| `FirebaseWalkCloudStore` | Authenticated Firestore upsert, explicit payload, server acknowledgement, bounded wait |
| `FirebaseWalkBootstrap` | Dependency initialization and connection to the existing manager; attach after console setup |
| `FirebaseLoginManager` / `LoginUIManager` | Email/password authentication and title-screen navigation |

The existing manager save/load methods remain available. Call
`LoadSavedWalks()` to retrieve local history, including after a restart. This
change does not add a new history screen or download cloud history.

Runtime scripts have an assembly definition so EditMode tests can reference the
actual game code. Existing script `.meta` GUIDs and scene references are retained.
The fake cloud service lives only in the Editor test assembly and is excluded
from Android builds.

## Next steps while console setup is managed by the project owner

Console screenshots supplied on 2026-09-09 confirm Email/Password and Google
sign-in are enabled and the `(default)` Firestore database has been created.
The user subsequently confirmed publishing the corrected rules after these
Rules Playground results: signed-out read denied, owner read allowed, and
another user's read denied. Publication is user-reported; the deployed rules
were not independently fetched through an authenticated API.

The repository-root `firestore.rules` contains the corrected walk-summary
rules, matched to the adapter's eight fields. These allow owner-only reads and
creates, allow retries to refresh only `uploadedAt`, and deny client deletion
and all unrelated paths. The numeric maximum uses a decimal literal because
the console rejected exponent notation. Read simulations passed in the console;
valid/invalid writes, server timestamps, and repeated writes still need testing.
No emulator tests or live Unity uploads have run yet.
The date checks validate UTC string shape, not calendar correctness; measurements
are constrained to the local C# types, not trusted activity/reward evidence.

1. Console setup is complete per the user's screenshots and publication report.
   Singapore (`asia-southeast1`) was the location selected in the guided setup;
   the final database details page was not inspected to independently confirm it.
2. Test the existing email/password login/registration flow from the title scene.
   The code does not create anonymous accounts or claim unowned local walks.
   `FirebaseWalkBootstrap.IsReady` means dependency initialization succeeded;
   it does not prove sign-in or server connectivity. Reuse the existing login
   manager instead of adding another login system.
3. Exercise the published rules with actual writes to
   `users/{ownerUserId}/walks/{id}`. The adapter and rule allowlist both contain
   `schemaVersion`, `id`, `startedAtUtc`, `endedAtUtc`, `steps`, `distanceMeters`,
   `durationSeconds`, and `uploadedAt`. Dates use UTC strings; `uploadedAt` is
   a Firestore server timestamp. An unchanged summary retry must be accepted;
   modified measurements, GPS fields, and another user's writes must be rejected.
4. `FirebaseWalkBootstrap` is attached to the existing persistent
   `StepCountManager` GameObject in `StepCounterTestAmar`. It checks
   dependencies and connects the adapter on Unity's main thread. Its
   `InitializeAsync()` can be called again after an initialization failure.
   The manager checks pending walks after configuration, save, resume, and every
   15 seconds, with persisted retry backoff up to one hour.
5. Test real sign-in/sign-out, account switching, permission denial, offline
   startup, reconnect, and lost acknowledgements on Android. Only a successful
   Firestore `SetAsync` task marks a record synced. The adapter stops waiting
   after 30 seconds or cancellation and leaves the record retryable. Firebase's
   underlying write can still complete later; stable owner/walk paths prevent
   duplicate records. A retry can refresh `uploadedAt` without changing totals.
6. Add cloud history reads and the history UI. Uploading summaries alone cannot
   restore GPS routes, and the current code does not download cloud records.

The adapter reads Firebase's current user for each operation and rejects owner
mismatches before submission. Disposing it pauses new uploads and cancels pending
waits. Firebase default instances are shared and are not disposed by the component.

API references: [Firebase Unity setup](https://firebase.google.com/docs/unity/setup),
[Firestore DocumentReference](https://firebase.google.com/docs/reference/unity/class/firebase/firestore/document-reference).

Do not increment rewards, totals, or counters in the upload adapter: retries can
occur even after a successful server write whose acknowledgement was lost.
If rewards/leaderboards are added, compute them through a trusted backend with
deduplication by walk ID. A client-provided step count is not proof of activity.

## Account ownership

The manager captures the authenticated UID when a walk starts. Changing accounts
during a walk does not change that walk's owner. Only pending records belonging
to the current UID are eligible for upload. Sign-out pauses new uploads; an
already submitted write may complete for its original account.

Walks created without authentication, including legacy saves, remain unowned and
local. A future account UI must let the player explicitly select local walks to
import, then call `AssignLocalWalkToCurrentAccount(walkId)`. Do not automatically
claim all local data on sign-in, and never reassign another account's walk.
Route data and local history currently remain on the device across account
changes; account-scoped history visibility/deletion must be designed with the
future login UI, especially for shared phones.

## Verification

Latest project review on 2026-09-09:

- Recompiled current runtime, test, and email/password login scripts against the
  installed Unity/Firebase assemblies. Also compiled the runtime with Android
  conditional code enabled; this was not a Gradle/IL2CPP APK build. The GPS script
  emits an existing obsolete `PermissionDeniedAndDontAskAgain` warning.
- Reran all 8 adapter tests in the managed runner: 8 passed, 0 failed.
- Verified Android package/config IDs agree, Auth/Firestore Android dependencies
  and generated AARs exist, title/game scenes are enabled in the correct order,
  and login UI references target the enabled game scene.
- Fixed the title scene's password field to use password masking. Added guarded
  Firebase initialization failure handling and auth-event/singleton cleanup to
  `FirebaseLoginManager`, including guards against callbacks after destruction.
- The live Unity editor was open. The full Unity EditMode suite, native Firebase
  initialization, and phone behavior were not rerun in this review. The prior
  17 persistence-test results below remain historical.
- At the end of the review, `FirebaseWalkBootstrap` was still unattached; no cloud connection was enabled
  during the review. The rules file and updated notes still need to be committed.

Following the review, the user authorized connecting cloud saves. The bootstrap
was attached to the walking scene's existing manager, and now logs
`Walk cloud sync initialized for the signed-in player.` once configuration
finishes. This enables runtime uploads but is not evidence of a successful
server write. Follow the live check below; the rules and cloud data were not
changed by the scene edit.

### First live check

1. Let Unity import the changes and reload the updated scenes if prompted.
2. Open `Assets/Scenes/Title Screen.unity` and enter Play mode. Open the login
   screen with Start, then register a dedicated test account or log in with an
   existing one. Successful authentication should load `StepCounterTestAmar`.
3. Wait for `Walk cloud sync initialized for the signed-in player.` in the Unity
   Console before starting a walk so ownership is captured at walk start.
4. Start and stop a short walk. Editor steps/GPS may be zero; that is valid for
   testing saving, not sensor accuracy. Expect `Summary synced to cloud` after
   server acknowledgement, or `Saved on device - sync pending` on failure.
5. In Firestore Data, inspect `users/{test-account-uid}/walks/{walk-id}`. The
   parent user document may not exist; its walks subcollection can still exist.
   Confirm the eight expected fields and server `uploadedAt` timestamp.
6. Retry/restart and confirm the same walk ID does not create another document.
   Test offline/reconnect and account isolation separately on the Android build.

On 2026-09-09, the runtime code and test assembly were compiled using the local
Unity assembly references and installed Firebase SDK. All **8 adapter tests
passed** in a standalone managed runner using Unity's Mono runtime, without
network calls. These cover acknowledgement gating, stable retry paths, payload
allowlisting, current-user changes, ownership rejection, write failures,
cancellation, timeout, disposal, and path validation. They do not exercise native
Firebase initialization, authentication, or a live database. Run
`FirebaseWalkCloudStoreTests` in Unity's EditMode Test Runner as well before
enabling the component. The prior persistence tests were not rerun in this check.

Verified on 2026-09-08 with Unity 6000.5.10f1: **17 EditMode tests passed,
0 failed**. Results are in `Walking Dog/Logs/walk-persistence-tests.xml` (ignored
generated output). A physical Android device and live Firebase were not tested.

In Unity, open **Window > General > Test Runner**, select **EditMode**, and run
`WalkPersistenceTests`. The tests use unique temporary directories and never
touch the player's saves. They cover actual Unity JSON serialization, restart
loading, old formats, duplicates, damaged files, backup recovery, disk failures,
retry/backoff, lost acknowledgements, cancellation, account isolation, payload
privacy, and the manager's end-walk save flow.

Command-line equivalent from the repository root (adjust the installed editor
path if needed; do not add `-quit` to a `-runTests` invocation):

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.5.10f1/Editor/Unity.exe' -batchmode -nographics -projectPath "$PWD/Walking Dog" -runTests -testPlatform EditMode -testResults "$PWD/Walking Dog/Logs/walk-persistence-tests.xml" -logFile "$PWD/Walking Dog/Logs/walk-persistence-tests.log"
```

Before Firebase setup, verify on a physical Android phone:

1. Enable airplane mode, start a walk, record steps, and stop. Confirm **Saved on
   device** and inspect the saved measurements and route.
2. Restart the app and inspect `LoadSavedWalks()` through a development history
   view or debugger. Confirm the previous completed walk remains available.
3. Complete a second walk and confirm both IDs remain separate.
4. Exercise several updates to the same file using the test adapter in a
   development-only harness to confirm atomic replacement/backup support on the
   target Android storage. Do not ship a fake adapter that claims cloud success.

## Scope and remaining limitations

- Only **completed** sessions are durable. Killing the app during an active walk
  can lose that unfinished walk; active-session checkpoint/resume and Android
  background sensor tracking are separate features.
- App uninstall or clearing application data removes local saves and queues.
  Real cloud acknowledgement plus a recoverable account is required for restore.
- No fake test can establish Firebase SDK compatibility, server rules, billing
  behavior, or Android filesystem/device behavior. Those need integration and
  device tests once the relevant setup is available.
- Cloud uploads contain summaries only. A separate bounded route-storage design
  is required before GPS history can be backed up remotely.
