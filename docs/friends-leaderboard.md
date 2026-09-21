# Friends and friends-only rankings

The connected leaderboard now has **Global** and **Friends** tabs, each supporting
all-time distance and steps. Global retains the existing top 50. Friends loads the
current player and every accepted friend directly, then ranks that group; a friend
outside the global top 50 is still included. Players without counted walks are
visible in friend management but do not receive a fabricated score or rank.

## Player flow

1. Open Leaderboard → Manage friends while signed in.
2. Copy your friend code and share it, or paste your friend's code and tap Send.
3. The recipient opens Manage friends and taps Refresh friends, then Accept or
   Decline. The sender can cancel a pending request.
4. Return to Rankings and select Friends. Remove friend ends the relationship
   for both players. It does not delete either player's walks or scores.

For this first version the code is the existing Firebase player UID, with a Copy
button. The recipient must open Manage friends once (or save a nickname in the new
build) to register their code. Codes are exact and case-sensitive. No email search,
contact upload, push notifications, or realtime listeners are introduced. Refresh
when reopening or using the refresh buttons; changes made on another device appear
on the next refresh. Global scores remain visible to signed-in users: the Friends
tab is a ranking filter, not a change to score privacy.

## Implementation

- `FirebaseLeaderboardService` implements `IFriendsService` and has a new scoped
  `LoadAsync` overload. The original overload still loads Global.
- `friendCodes/{uid}` stores only a public display name and update timestamp.
  Signed-in users may look up an exact code; listing the directory is denied.
  Private leaderboard profiles, walks and GPS data retain their existing rules.
- `friends/{uid}/links/{otherUid}` and its mirror store `requestedBy`, `status`
  (`pending` or `accepted`) and `updatedAt`. Both copies must be created, accepted,
  or deleted atomically. Only the recipient can accept. Both participants may
  cancel/decline/remove; unrelated users cannot read or write the relationship.
- The service uses transactions, server reads, a 20-second timeout, cancellation,
  account-change checks and bounded batches of parallel reads. Friendship lists
  are paged in groups of 50. Reads across multiple players are not an atomic
  snapshot; the ranking reflects relationships observed when refresh began.
- Native Firebase writes can finish after a timeout. Refresh to check the result
  before retrying. Duplicate sends cannot create duplicate friendships.
- `LeaderboardPanelUI` creates the tabs and management button from existing UI
  templates at runtime. `FriendsPanelUI` creates the scrollable overlay. Existing
  scene references and artwork are reused; no scene migration is required.

## Verification and rollout

Run the Unity EditMode suite and `npm.cmd run test:emulator` from `functions/`.
The emulator suite covers code privacy/ownership, mirrored writes, forged sender,
self/unknown requests, acceptance permissions, crossed requests, cancellation,
decline, removal and protection of the existing leaderboard/walk behavior. Unity
tests cover friends ranking inclusion, both metrics, ties and tab request races.

Verified locally on September 21, 2026: **73/73 Unity EditMode tests passed**.
The Firestore suite passed **20/21**, including **all five new friendship tests**.
The existing `Spark concurrent walks preserve totals, including fractional distance`
test fails with `permission-denied`; it also fails against the original `HEAD`
rules in an isolated baseline run. That pre-existing scoring issue is not changed
by this feature. Logs are under `Walking Dog/Logs/friends-*.log` (ignored output).
The rankings and friend manager were also rendered and visually checked at
720×1280 and 946×2048. `LeaderboardSceneSetup.CaptureFriendsPreview` reproduces
these previews using local sample data without saving the scene or contacting Firebase.

Deploy the updated **Firestore rules before distributing the new client**. Both
friends management and nickname saves use the new `friendCodes` path. The feature
uses the existing Spark-compatible client/rules architecture; no new Cloud Function,
billing upgrade, composite index or historical backfill is required.

From `functions/`, using the project owner's existing Firebase CLI login:

```powershell
npx firebase deploy --only firestore:rules --project walky-aa25c --config ../firebase.json
```

This code change does not deploy Firebase or generate a new APK. Before release,
check the full flow with two accounts on devices, including closing the panel,
signing out, reconnecting after an interrupted request, and friends with no walks.
