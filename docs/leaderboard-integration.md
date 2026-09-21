# Leaderboard data and UI handoff

> Current feature: see [Friends and friends-only rankings](friends-leaderboard.md)
> for the scoped service, connected runtime UI and updated deployment requirements.
> The original handoff below predates the connected UI and Spark-compatible client
> aggregation now implemented in `FirebaseLeaderboardWriter` and `firestore.rules`.

This feature adds an all-time distance/steps leaderboard without modifying scenes,
prefabs, existing UI scripts, walk recording, login, or the walk upload payload.
The existing `FirebaseWalkBootstrap` also stays unchanged. Nothing runs until UI
code creates and calls the new service. No live Firebase changes are included.

## Ownership boundary

- UI teammate: layout, button wiring, list rows, loading/error/empty states,
  nickname input, opening/closing the panel, refresh timing.
- Data/backend: `Assets/Scripts/Leaderboard/`, `functions/`, `firestore.rules`,
  `firebase.json`, and the dedicated tests. These can be merged independently of UI.
- No mandatory new MonoBehaviour, scene component, prefab, or serialized reference.
  All public types are in `WalkingDog.Leaderboards` in the existing runtime assembly.

## API contract

`ILeaderboardService` allows the UI teammate to supply a mock while designing the
screen. `FirebaseLeaderboardService` is the real implementation. Create, call and
dispose it on Unity's main thread. Reuse the existing bootstrap and authentication.

```csharp
using WalkingDog.Leaderboards;
using System.Threading;

// bootstrap is the existing FirebaseWalkBootstrap on the active walk manager.
// Create once for the panel/controller lifetime; dispose on destruction.
ILeaderboardService leaderboard = await FirebaseLeaderboardService.CreateAsync(bootstrap);

// Cancel an earlier request when closing, refreshing or switching metric.
// In a real controller, also use a request-generation number so a late response
// can never repaint a closed panel or overwrite a newer selection.
var request = new CancellationTokenSource();
LeaderboardSnapshot data = await leaderboard.LoadAsync(
    LeaderboardMetric.Distance, request.Token); // Or LeaderboardMetric.Steps

foreach (LeaderboardEntry entry in data.Entries)
{
    // entry.PlayerId == data.CurrentPlayerId identifies the highlighted row.
    // DisplayName, TotalDistanceMeters / 1000d, TotalSteps, CompletedWalkCount.
}

// data.CurrentPlayer is fetched even outside the top 50; null means no counted
// walk yet. Do not label an outside-top-50 player as rank 51 or rank 0.
await leaderboard.SaveDisplayNameAsync("Mochi Walker", request.Token);

// Panel close: cancel request. Controller destruction: dispose both resources.
request.Cancel();
request.Dispose();
leaderboard.Dispose();
```

The snippet shows calls, not a complete UI lifecycle implementation. The controller
must catch exceptions and manage its requests. Suggested behavior:

- Disable/replace the panel with a sign-in prompt if `AuthenticatedUserId` is empty.
- Clear displayed player data on sign-out/account change. The service rejects
  in-flight results after auth changes; it does not clear already-rendered labels.
- `OperationCanceledException`: discard quietly. `TimeoutException`, Firebase
  permission/network errors: show retry. Never present a failed read as zero scores.
- Both rankings return at most 50 entries, sorted descending on their metric.
  Equal scores use Firestore's descending document-ID tie order; list positions
  are ordinal, not shared ranks. There is no global rank calculation outside 50.
- Distance stays in metres internally. Format kilometres/steps in the UI.
- Names use 3–24 ASCII letters, digits, spaces, underscores or hyphens, beginning
  with a letter/digit. `LeaderboardNames.IsValid` supports inline validation.
  Names need not be unique; `PlayerId` is identity. Default names are `Walker-`
  plus an eight-character hash, never email addresses. No avatar field in v1.
- Reads explicitly request the server, with a 20-second timeout. There is no
  realtime listener or offline leaderboard cache. Refresh on opening or manually;
  do not read every frame. A timeout/cancellation does not cancel a native Firebase
  write; a nickname save may complete later, and repeating the same name is safe.
- Scores and renamed labels appear after backend processing, not immediately on
  local save. A successful walk upload can precede its leaderboard update.
- Own totals outside the list require a separate read, so this is not an atomic
  snapshot across all players. Avoid promises of exact live ranking.

## Stored data and aggregation

Existing `users/{uid}/walks/{walkId}` summaries remain owner-readable only.
Only completed uploaded walks count; steps represent recorded game walks, not
all-day pedometer data. All-zero test walks are excluded; steps-only or
distance-only walks are allowed. Weekly ranking and territory ranking are deferred.

`leaderboards/allTime/players/{uid}` is readable by signed-in players and contains:

| Field | Type |
| --- | --- |
| schemaVersion | integer, 1 |
| displayName | string |
| totalDistanceMeters | number |
| totalSteps | integer |
| completedWalkCount | integer |
| updatedAt | server timestamp |

All client score writes are denied. `leaderboardProfiles/{uid}` is owner-only and
accepts only `displayName` and server `updatedAt`. A backend trigger copies the
latest name into the public entry. Choosing a name alone does not create a score.

`countLeaderboardWalk` triggers on creation of a saved walk. Its transaction reads
the actual walk, current totals, profile and `leaderboardReceipts/{uid}/walks/{walkId}`.
It writes the totals and receipt atomically. Duplicate delivery, unchanged upload
retries, concurrent walks and historical backfill cannot count the same walk twice.
Receipts are backend-only. Name processing reads current profile state, so delayed
events cannot roll back a newer name. Functions use retry on transient failures.

The backend validates numeric shape/ranges, not physical activity. Existing client
measurements remain untrusted. This is suitable for casual comparison, not rewards
or anti-cheat enforcement. Admin edits/deletions of already-counted walks do not
automatically adjust totals; do not delete receipts or edit totals independently.

## Local verification

Node 22 and Java 21+ are recommended (Unity's bundled Android JDK can be used).
From `functions/`:

```powershell
npm.cmd ci --ignore-scripts
npm.cmd run test:emulator
```

The tests use `demo-walking-dog` and reject a missing/nonlocal emulator address.
They check duplicate/concurrent counting, isolation, rollback, current names,
both sorts, top-50 exclusion, score/receipt protection, nickname validation and
the existing private walk rules. The Firestore emulator tests invoke the same
aggregation functions directly; they do not exercise deployed trigger delivery.

Run `LeaderboardTests` in Unity EditMode for the public service, parsing,
account switching, cancellation, timeout, disposal and input validation.

Verified on September 16, 2026: all **59 Unity EditMode tests passed**, including
the five new leaderboard tests, and all **9 Firestore emulator tests passed**.
Unity results: `Walking Dog/Logs/leaderboard-tests-final.xml` (ignored output).
The function exports and backfill JavaScript also passed load/syntax checks.
No Android device test, APK build, production deployment or live backfill was run.

## Firebase rollout (project owner)

This repository does not select a default Firebase project, upload credentials,
enable billing, deploy functions/rules or run a production backfill automatically.
Deploy after reviewing the change with the team:

1. Confirm the real Firestore location; functions currently specify
   `asia-southeast1` to match the setup notes. Confirm Blaze billing is available.
2. Install dependencies above, run emulator and Unity tests, and inspect any
   deployed rules to preserve changes made outside this repository.
3. From `functions/`, after authenticating the Firebase CLI:

   ```powershell
   npx firebase deploy --only firestore:rules,functions:leaderboard --project walky-aa25c --config ../firebase.json
   ```

4. Use two dedicated test accounts to confirm names, private walks, both rankings,
   repeat uploads and offline/reconnect behavior. No extra composite index is
   needed for the single-field descending queries.
5. Existing walks do not generate creation events on deployment. With Admin
   Application Default Credentials configured outside the repository, first inspect:

   ```powershell
   node backfill.js --project=walky-aa25c
   ```

   This only reads and reports eligible/empty/invalid counts. Then explicitly run
   `node backfill.js --project=walky-aa25c --apply` to count historical walks. The
   scan is paged and safe to rerun or overlap with live triggers. All existing
   nonempty valid summaries are included, including old test activity; choose the
   intended historical scope before running it against production.

Keep Admin credentials out of Unity/Assets and source control. The Unity client
uses its existing Firebase configuration and signed-in player's credentials.

References: [Firestore triggers](https://firebase.google.com/docs/functions/firestore-events),
[field-level rules](https://firebase.google.com/docs/firestore/security/rules-fields),
[Cloud Functions deployment](https://firebase.google.com/docs/functions/get-started).
