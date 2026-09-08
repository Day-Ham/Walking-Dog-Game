# Local saves and future Firebase integration

The app does not currently connect to Firebase. No Firebase SDK, project,
credentials, database, or authentication provider is required for these changes.

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

The existing manager save/load methods remain available. Call
`LoadSavedWalks()` to retrieve local history, including after a restart. This
change does not add a new history screen or download cloud history.

Runtime scripts have an assembly definition so EditMode tests can reference the
actual game code. Existing script `.meta` GUIDs and scene references are retained.
The fake cloud service lives only in the Editor test assembly and is excluded
from Android builds.

## Connecting Firebase later

1. Register the Android app, import Firebase Authentication and Firestore, and
   add the configuration file. Initialize the SDK and authentication before
   providing a usable user ID.
2. Implement `IWalkCloudStore` in a `FirebaseWalkCloudStore`. Its
   `AuthenticatedUserId` must reflect the current Firebase user, or an empty
   string while signed out. Do not cache a previous user's ID indefinitely.
3. Implement `UploadSummaryAsync(ownerUserId, summary, cancellationToken)` with a
   Firestore **set/upsert** at `users/{ownerUserId}/walks/{summary.id}`. Verify the
   authenticated UID matches the requested owner before writing. Map the DTO's
   fields explicitly and add a server-generated `uploadedAt` timestamp. Complete
   the task only when the server acknowledges the write, never merely when it
   enters an SDK cache. Propagate failure/cancellation to the caller. Firebase
   writes that cannot be canceled must still be safe to repeat after cancellation.
4. On Unity's main thread call
   `StepCountAndGpsManager.Instance.ConfigureCloudSync(adapter)`. The manager
   checks pending walks immediately, after a completed save, on resume, and every
   15 seconds while configured. Retries use persisted exponential backoff, up to
   one hour. Configure after the singleton exists. Wait for an active sync to
   finish before replacing the adapter; normal authentication changes should be
   reflected through the existing adapter's user-ID property.
5. Deploy rules requiring the authenticated user to own the path and validating
   the exact allowed fields, types, and ranges. Client-side ownership checks are
   not a substitute for Firebase rules.
6. Test real account changes, permission denial, offline startup, reconnect,
   acknowledgement loss, and history retrieval before enabling cloud saves for
   players. Reading Firestore history and merging it into the UI is a separate
   integration step; summaries alone cannot restore full GPS routes.

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
