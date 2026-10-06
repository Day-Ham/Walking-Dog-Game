# Following and walking activity prototype

October 5 automatic sharing: Walk sharing ON now uploads existing owned GPS
routes saved on this phone and future completed routes after summary sync. There
is no per-route share button. Uploads retry after reconnecting/reopening, preserve
existing cloud routes, and stop when sharing is OFF or the account changes.
All 121 Unity EditMode tests, 51 Firebase emulator tests and map script checks
passed. Firestore rules were deployed to `walky-aa25c` and the live release
`365715ca-a8e2-49c1-8e60-6a6819fb2fc1` was verified to exactly match this file's
repository rules. Build/install the updated client for phone testing; no APK or
physical-device validation was produced by this change.

October 1, 2026: implemented and the rules deployed to `walky-aa25c`. Eight Unity
EditMode tests and all 45 Firebase emulator tests passed. Activity and Followers
were rendered and inspected at 720 × 1280, with Activity also checked at
946 × 2048. This change does not build an APK; two-device validation is pending.

October 2 route repair: the live rules were missing the `sharedRoutes` section,
which denied route reads on both existing and fresh installations. Deployed the
tested rules and verified the live release matches the repository. Owners now
fall back to their own saved GPS if the cloud read fails; account changes cancel
that fallback. Vector and raster maps fit the selected route. Validation: 21 Unity
EditMode tests, 48 Firebase emulator tests, and the JavaScript map checks passed.
The updated app still needs rebuilding and validation on physical devices; no
APK was built during this repair.

The existing `Profile Screen` in `StepCounterTestAmar.unity` now connects to real
Firebase accounts. Open **Profile** from the walking screen. Its original profile
header, follower/following controls, list, profile-card artwork and Follow button
are reused. Additional controls are serialized scene objects and can be edited in
the Inspector. `SocialSceneSetup.Connect` is an explicit, repeatable editor command
for reconnecting this layout; it is not an import-time migration.

## Try it with two accounts

1. Sign in as Alice and open Profile. Copy Alice's eight-character player code.
2. Turn **Walk sharing: ON** to let followers read Alice's past and future synced
   walks and GPS routes. Existing routes saved on this phone upload automatically;
   future completed routes upload after their summaries sync. Sharing defaults to
   off; opening Profile never enables it.
3. As Bob, open Profile and enter Alice's code, or find Alice in Discover. Tap
   **Follow**, then **View walks** or **Activity**.
4. Finish and sync a walk as Alice. Refresh Bob's Activity tab to see its date,
   distance, steps and duration. Opening Profile also reloads the feed.
5. Alice's **Followers** list shows Bob with **Follow back**. Following back is
   independent: neither person needs to accept a request. Bob must enable his own
   sharing for Alice to see Bob's walks.
6. Unfollow Alice as Bob: Alice's walks disappear from Bob's feed and future reads
   are denied. Alice's independent follow of Bob is unaffected. Turning sharing
   off also denies subsequent follower reads without deleting relationships.

Activity includes your own walks even when your sharing is off. Your header and
counts always refer to your account; View walks adds the selected person's card
and recent walks below it. That profile card then reads **Viewing walks**. Each
individual walk has **View details**, which opens a popup with the walker, finish
date/time, distance, steps and duration (including seconds). **Back to walks** or
Back/Escape closes the popup without refreshing or resetting the list position.
Refreshing, closing Profile, or changing accounts clears the popup.
The popup also displays the session's saved GPS route. The owner can view the
recording saved on that phone, including when the cloud request fails. To make
the map available to followers and other installations, keep **Walk sharing: ON**
on the recording phone. No per-walk share action is needed. The persistent manager
checks for routes at startup, on resume, after changing sharing or saving a walk,
and every 15 seconds while the app is running. Offline failures retry from saved
local walks, even after restart. Already uploaded cloud routes are preserved.
Turning sharing OFF immediately denies new follower reads of summaries and routes
and stops automatic route uploads. Re-enabling sharing restores access and uploads
remaining routes. If a route was never uploaded and the original
local recording was deleted, its coordinates cannot be recovered from a summary.
Missing routes show an explanation. Saved maps fit the session's entire route;
manual panning remains available on the vector map.
Back from a viewed profile returns to Activity; Back
from a main tab closes the screen and restores the live map.

## Firebase data and rollout

- `social/{follower}/following/{followed}` and
  `social/{followed}/followers/{follower}` are atomic, mirrored indexes, each
  containing only `createdAt`. Only the follower may create/delete that pair.
  A reverse follow is a separate pair. Existing accepted-friends rankings and
  requests continue to use their original collections.
- `socialProfiles/{uid}` contains `shareActivity` and `updatedAt`. Only its owner
  may change it. Missing settings mean private activity.
- Activity queries read the existing immutable `users/{uid}/walks` summaries.
  Rules require ownership, or a follow plus the author's sharing opt-in. Wallets,
  inventory, recovery data and continuous-step streams retain their existing
  access controls. No route coordinates are included in summary documents.
- `users/{uid}/sharedRoutes/{walkId}` stores GPS routes under the account's sharing opt-in,
  bounded to 2,000 samples / 200,000 JSON characters. The owner can read it; a
  follower can read it while the author's summary sharing is enabled. Collection
  listing is denied. Only the owner can upload a route for an existing summary
  or delete it. Coordinates and segment gaps are shared; sensor metadata is not.
- Names, photos and player codes reuse `friendCodes`, `friendCodeLookup`, and
  `friendCodeOwners`. Profile entry registers the signed-in user's name/photo.
- Queries use `Source.Server`; failures show a retry message, not invented empty
  totals. Closing the panel or changing accounts cancels the UI request and
  removes its identity and rows. An acknowledged or timed-out write can still
  complete at Firebase; Refresh reconciles the result.

The changed `firestore.rules` have been deployed to the live database. No new
Cloud Functions, composite indexes or walk backfill are required. Build the updated
Unity client for both test devices. To deploy these rules again:

```powershell
cd functions
npx firebase deploy --only firestore:rules --project walky-aa25c --config ../firebase.json
```

The October 5 rules also support the latest pulled milestone wallet field,
`milestoneRollsClaimed` (missing means zero for older wallets). Each free-roll
claim increments it once, within the earned-points limit, and creates an immutable
`users/{uid}/milestoneReceipts/{receiptId}` document with `milestone` and `claimedAt`
in the same transaction. The wallet's `lastWalkId` identifies that receipt during
a claim, just as `activity` identifies continuous rewards. Normal earnings and
spending preserve the claim count. No production balances or histories were
modified; this rollout changes permissions and validation only.

Rules validate both halves of each follow in the same commit using
[`getAfter`](https://firebase.google.com/docs/firestore/manage-data/transactions).
Each author's activity is queried separately because
[Firestore rules do not filter query results](https://firebase.google.com/docs/firestore/security/rules-query).

## Prototype boundaries

- Discover shows up to 50 ranked walkers. Exact player-code lookup reaches users
  outside those 50, including users with no ranked walks. There is no global
  fuzzy name search or contact import.
- Activity loads the latest 10 completed walks per author, sorted by finish time,
  with a maximum of 100 feed cards. Followers/following counts load all graph pages.
  Feed fan-out is suitable for a small prototype; large networks should use a
  paginated server-built feed before wider rollout.
- Refresh/reopen updates the feed; there are no live listeners or push alerts.
  Continuous steps by themselves are not a feed post. A saved, synced walk is.
- A user who enables sharing lets any signed-in user follow them and read these
  summaries and automatically shared routes. There are no approval requests,
  blocking, likes or comments. Turning sharing off stops new reads; it cannot
  retract data already read.

## Validation

`SocialTests` and `SocialSceneTests` cover feed ordering, deduplication, existing
scene bindings, follow-back state, profile navigation, sharing opt-in, code lookup,
late reads, account changes and error recovery. The Firebase emulator suite covers
directional follow/unfollow, atomic mirrors, impersonation, self-follow, unknown
players, unauthenticated access, sharing revocation and summary privacy.

```powershell
cd functions
npm run test:emulator
```

`SocialSceneSetup.CapturePreview` renders Activity and Followers at phone sizes
using editor-only fixture data, which is never saved or sent to Firebase.
