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

Friend codes now contain **eight characters**, shown as `ABCD-EFGH`, with a Copy
button. Open Manage friends once to register a permanent code. Short codes accept
lowercase and the optional hyphen; ambiguous I/O/0/1 characters are excluded.
Existing UID codes still work and remain case-sensitive. No email search,
contact upload, push notifications, or realtime listeners are introduced. Refresh
when reopening or using the refresh buttons; changes made on another device appear
on the next refresh. Global scores remain visible to signed-in users: the Friends
tab is a ranking filter, not a change to score privacy.

## Implementation

- `FirebaseLeaderboardService` implements `IFriendsService` and has a new scoped
  `LoadAsync` overload. The original overload still loads Global.
- `friendCodes/{uid}` stores a public display name, optional profile picture and update timestamp.
  Signed-in users may look up an exact code; listing the directory is denied.
  Private leaderboard profiles, walks and GPS data retain their existing rules.
- `friendCodeOwners/{uid}` and `friendCodeLookup/{code}` reserve one immutable
  short code per account atomically. Collision retries generate another random
  code; nobody can overwrite an existing claim. Only the owner can read their
  reservation; signed-in users can perform exact code lookups, but cannot list codes.
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
- The tabs and animated friend manager are scene objects with Inspector wiring.
  Friend rows and leaderboard cards remain dynamic. `ProfilePhotoUI` renders the
  same portrait in both views, with initials and an identity-based colour as fallback.

## Profile pictures

Google sign-in pictures are synced when opening rankings or Manage friends.
The leaderboard also provides **Phone photo** and **Use Google photo** buttons,
plus your current picture even before your first ranked walk. Phone photo opens
Android's system document picker, which grants access only to the selected image.
The image is orientation-corrected, centre-cropped and resized to 192×192, then
encoded as a JPEG of at most 24,000 bytes. The thumbnail is saved on the public
profile, not the original image or its EXIF metadata. This uses the existing
Firestore architecture and does not require a Storage SDK, bucket or billing change.

An uploaded picture takes precedence over Google refreshes, including after
relaunching or signing in on another device. Use Google photo clears that override;
email-only accounts then display initials. Cancelling the picker leaves the current
picture unchanged. Phone selection is Android-only; the Editor can render saved
thumbnails and test the UI but cannot open a phone gallery.

The public `photoUrl` field contains an HTTPS Google photo URL, an inline JPEG
thumbnail, or an empty string. Rules cap its length at 32,768 characters and restrict
the accepted URL/encoding formats. Name changes merge the profile so they preserve
its photo. Rankings fetch public profiles in batches of ten (one additional read per
displayed player); image-download failures keep initials visible. Closing rows aborts
downloads and releases their textures. No full-resolution originals are stored.

The scene also fixes the misassigned friend-code/status labels and missing friends-panel
reference. Leaderboard visibility now follows its controller root, so the hidden
screen does not suspend the walking map at startup. The existing exit animation
disables that root after its last frame; the friends animation still closes only itself.

## Verification and rollout

September 23, 2026: repaired the scene-backed friend manager's serialized
references and restored the Send, Refresh and Copy handlers. Opening the panel
initializes its controls without duplicating Inspector callbacks. The displayed
and copied code comes from `GetFriendCodeAsync`, and friend rows again use
`ProfilePhotoUI`. These are client/scene fixes; they do not change Firestore rules.
All 78 Unity EditMode tests passed in an isolated project copy, including points
recovery, missing-manager display, short-code copying and duplicate-listener
regressions. The map route/territory script also passed. No physical-device check
or Firestore emulator rerun was performed for these client fixes.

**Deployment verified September 22, 2026, 05:09 UTC:** the live `walky-aa25c`
Firestore rules now exactly match this repository, including `photoUrl`,
`friendCodeOwners` and `friendCodeLookup`. The previously active September 21 rules
lacked all three and blocked both photo saves and fresh-account registration.
The fix is server-side; devices already running the profile-picture APK can reopen
the screens and retry without reinstalling or rebuilding. The fresh-account
regression passed; the emulator suite is now **24/25**, with only the same existing
fractional-distance concurrent-scoring failure. Device retesting is still needed
to confirm the complete Android picker-to-save flow.

If an existing account can load rankings but both photo options fail, and a fresh
account has no friend code, check the **deployed** rules first. Rules that only
allow `displayName` and `updatedAt` on `friendCodes` reject the new client's initial
profile registration (which includes `photoUrl`, even when empty). Rules without
`friendCodeOwners` and `friendCodeLookup` also reject short-code registration.
Reinstalling or clearing app data cannot correct these server permissions. Deploy
the rules from the same feature version, reopen the leaderboard and Manage friends,
and retry the photo selection. Google-photo restoration requires an account with
a Google photo; email-only accounts otherwise return to initials.

The emulator regression `fresh account registers its profile and short code, then
saves and restores a photo` covers an account with no existing profile, walks,
friend links or code, plus reading the same code from a second client installation.

September 22, 2026: **76/76 Unity EditMode tests passed** after the short-code,
profile-picture and scene-wiring changes. Android `ProfilePhotoPicker.java` also
compiled against the installed Android SDK. Initial Firestore emulator run: **23/24 passed**,
including every new short-code and photo test. The one failure is the previously
documented fractional-distance concurrent-scoring test below. That initial check
preceded the rules deployment recorded above; no APK build or physical-device
photo-picker test was performed locally.

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

Deploy the updated **Firestore rules before distributing the new client**. Short
codes require the two reservation paths, and pictures require the optional public
profile field. The feature
uses the existing Spark-compatible client/rules architecture; no new Cloud Function,
billing upgrade, composite index or historical backfill is required.

From `functions/`, using the project owner's existing Firebase CLI login:

```powershell
npx firebase deploy --only firestore:rules --project walky-aa25c --config ../firebase.json
```

This code change does not deploy Firebase or generate a new APK. Before release,
check the full flow with two accounts on devices, including closing the panel,
signing out, reconnecting after an interrupted request, and friends with no walks.
