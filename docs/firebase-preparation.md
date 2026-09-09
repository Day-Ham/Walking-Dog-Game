# Local saves and Firebase integration

Firebase Auth and Firestore SDK 13.16.0 and the Android configuration for
`walky-aa25c` are present. The Android package matches Unity's
`com.TheVeryEvilCompany.WalkingDog`. The adapter and initialization component
are implemented, but the component is not attached to any scene, so gameplay
still saves locally. Console setup, login, rules, and live testing remain pending.

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

The existing manager save/load methods remain available. Call
`LoadSavedWalks()` to retrieve local history, including after a restart. This
change does not add a new history screen or download cloud history.

Runtime scripts have an assembly definition so EditMode tests can reference the
actual game code. Existing script `.meta` GUIDs and scene references are retained.
The fake cloud service lives only in the Editor test assembly and is excluded
from Android builds.

## Next steps while console setup is managed by the project owner

1. Have the project owner confirm the enabled sign-in provider, a Standard
   edition `(default)` Firestore database, its region, and its current rules.
2. Implement the selected login flow. The new code does not choose a provider,
   create an anonymous account, or claim unowned local walks. A signed-out user
   continues saving locally. `FirebaseWalkBootstrap.IsReady` means dependency
   initialization succeeded; it does not prove sign-in or server connectivity.
3. Prepare and have the owner review/publish rules for
   `users/{ownerUserId}/walks/{id}`. Require matching authenticated ownership and
   validate exactly `schemaVersion`, `id`, `startedAtUtc`, `endedAtUtc`, `steps`,
   `distanceMeters`, `durationSeconds`, and `uploadedAt`. Dates currently use UTC
   strings; `uploadedAt` is a Firestore server timestamp. Validate types/ranges,
   bind `id` to the document ID, and allow safe retries of unchanged summaries.
   Rules must be integrated with any existing project rules; none were deployed.
4. Add `FirebaseWalkBootstrap` to the existing persistent
   `StepCountAndGpsManager` GameObject in the intended startup scene. It checks
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
