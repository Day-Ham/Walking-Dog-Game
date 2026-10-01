# Following and walking activity prototype

October 1, 2026: implemented and the rules deployed to `walky-aa25c`. Eight Unity
EditMode tests and all 45 Firebase emulator tests passed. Activity and Followers
were rendered and inspected at 720 × 1280, with Activity also checked at
946 × 2048. This change does not build an APK; two-device validation is pending.

The existing `Profile Screen` in `StepCounterTestAmar.unity` now connects to real
Firebase accounts. Open **Profile** from the walking screen. Its original profile
header, follower/following controls, list, profile-card artwork and Follow button
are reused. Additional controls are serialized scene objects and can be edited in
the Inspector. `SocialSceneSetup.Connect` is an explicit, repeatable editor command
for reconnecting this layout; it is not an import-time migration.

## Try it with two accounts

1. Sign in as Alice and open Profile. Copy Alice's eight-character player code.
2. Turn **Walk sharing: ON** to let followers read Alice's past and future synced
   walk summaries. Sharing defaults to off; opening Profile never enables it.
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
  access controls. No route coordinates are uploaded or included in the feed.
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
  summaries. There are no approval requests, blocking, likes, comments or route
  maps. Turning sharing off stops new reads; it cannot retract data already read.

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
