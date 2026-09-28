# Continuous steps and territory walks

September 28, 2026: steps, points and the steps leaderboard no longer require
starting or finishing a territory walk.

## Player behavior

- The main step display is the current account's cumulative steps recorded on
  this installation by the new tracker. It survives reopening the app and is
  never reset by Start/Finish Territory Walk. It is not a daily counter or an
  imported all-time health total.
- Every ten new tracked steps earns one wallet point. Leftovers carry across
  uploads, app restarts and territory walks. Account-wide cloud aggregation also
  carries leftovers across device streams. Example: 105 + 208 + 47 = 360 steps,
  earning 36 points, regardless of where the territory walk starts and ends.
- A territory walk still records its own route, distance, duration and steps for
  history. Its steps are already part of continuous activity and are not awarded
  or added to leaderboard steps again. Existing territory qualification rules
  remain in place. Distance rankings and completed-walk counts still use walks.
- Rewards sync approximately every 15 seconds while the app is running. Offline
  steps remain saved locally; the wallet shows its last server-confirmed balance
  with a pending indicator until synchronization succeeds.
- Signed-out activity stays local. Signing in does not import guest steps or move
  activity between accounts. The public leaderboard aggregates each account's
  legacy walk steps plus new continuous steps across installations.

This change makes tracking independent of the walk button. It does not add an
always-running Android step service or promise tracking after force-stop or
while the app is closed. The existing detector pauses with Unity and may reconcile
from the hardware counter on resume. Physical-device checks remain necessary.

## Persistence and synchronization

`ContinuousSteps` maintains an account-scoped UUID stream and absolute total under
`Application.persistentDataPath/WalkSessions/ContinuousSteps`. Changed totals are saved using
flushed temporary files, atomic replacement and a backup. Sensor corrections
cannot re-credit an already observed count. A new sensor source begins at zero;
saved totals are separate from that source's app-session baseline.

An upload atomically updates:

- `users/{uid}/stepStreams/{streamId}`: the last credited absolute stream total.
- `users/{uid}/activity/main`: account-wide continuous steps and last stream ID.
- `users/{uid}/wallet/main`: only the newly crossed ten-step thresholds.
- `leaderboards/allTime/players/{uid}`: only the newly observed steps, plus the
  current wallet balance; route distance and walk count are preserved.

Retrying the same or an older total has no effect. Concurrent streams read and
update the same aggregate transactionally. Local acknowledgement only follows
server success; late completion after timeout can be retried safely. Sign-out
cancels pending work and prevents late results from updating another account's UI.
Rules require the cursor, aggregate, wallet and score to agree and commit together.
As before, accounting rules validate client-submitted measurements, not physical
activity. Multiple devices reporting the same physical movement are not deduplicated.

## Migration and rollout

New walks carry `stepAccountingVersion: 1` locally and in the coordinate-free
cloud summary. Legacy summaries omit it and retain existing per-walk rewards,
rounding and receipts. Existing wallet balances, spending and leaderboard totals
are preserved. Historical steps are not added to the new activity aggregate.

An unfinished legacy walk retains legacy accounting when recovered: new steps
inside that one recovered session remain on the old per-walk reward path and
are excluded from the new continuous stream, preventing double credit. Once it
finishes, subsequent steps and new territory walks use independent accounting.

Deploy the updated `firestore.rules` before distributing the new client. The new
activity paths are denied by old rules; local steps remain retryable until the
rules are deployed. If the optional Node leaderboard functions are deployed,
update `functions/leaderboard.js` with this release as well, since its legacy
version would count territory steps a second time. The initial source edit did
not deploy rules or backfill production data.

### September 28 sync-pending fix

The phone test exposed an undeployed rules update. Live rules were last published
September 24 and denied both continuous-step paths and the new walk accounting
marker. The app could read its existing wallet but could not credit new activity.

The live rules also contained gacha changes absent from the repository. The rollout
preserves owner-scoped spending, spend receipts and inventory, public avatar
profile reads for signed-in players, and equipment metadata. Score updates still
require exact accounting; cosmetic changes cannot also change leaderboard totals.
The existing gacha flow remains client-controlled, as in the previous live rules.

The merged rules passed **41/41 emulator tests**, were deployed to `walky-aa25c`
at **2026-09-28 08:05:57 UTC**, and were read back to verify an exact match with
`firestore.rules`. No balances or player documents were edited manually. Pending
device records retry normally once online; a new APK is not required for this
permissions fix. Function listing was unavailable, so no optional Cloud Functions
deployment was performed or verified during this rollout.

Separately, the territory status color now updates during an active walk and
resets when the walk finishes or the closure cue is unavailable. The previous
active-walk early return skipped color updates until the walk ended. This source
fix requires the next client build; **96/96 Unity tests** passed.

Audit files in ignored `Logs`: `continuous-rollout-before.rules`,
`continuous-live-inspection.rules`, `continuous-rollout-deploy.log`,
`continuous-rollout-emulator.log`, and `sync-status-tests.xml`.

## Validation

Verified in this change: **96/96 Unity EditMode tests** and **39/39 Firestore
emulator tests** passed. Logs are in the ignored repository `Logs` directory:
`continuous-steps-tests.xml`, `continuous-steps-tests.log`, and
`continuous-steps-emulator.log`. The leaderboard writer now also retries bounded
permission-denied contention, so the previously failing concurrent fractional-
distance test passes without weakening the accounting rules.
The updated walk controls passed text-fit checks at 720 × 1280, 946 × 2048 and
1536 × 2048. The phone HUD and territory summary were rendered and visually
inspected; the layout log is `Logs/continuous-steps-layout.log`.

Unity tests cover persistence, sensor correction, account isolation, late
acknowledgement, backup/corruption handling, and steps before/during/after a
territory walk. Firestore emulator tests cover carried leftovers, duplicate and
concurrent uploads, cross-device streams, legacy preservation, territory
double-credit prevention, private cursors, and invalid atomic writes.

On a phone: count steps without starting a walk, finish a qualifying territory
loop, then keep walking. Confirm the total continues, points cross each ten-step
threshold only once, and territory is still claimed. Repeat offline/reconnect,
reopen, sign-out/sign-in, and screen-lock/resume checks. Do not infer closed-app
tracking from successful foreground tests.
