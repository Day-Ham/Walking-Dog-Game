# Dog race and training foundation

October 9, 2026 validation: all 133 Unity EditMode tests and 55 Firebase emulator
tests passed. Training was rendered at 720 × 1280 and 946 × 2048; racing and finish
results were rendered at 720 × 1280 and visually inspected. The training rules
were deployed to `walky-aa25c`; release `8cd50551-62a8-4897-8264-55adddab8310` was
verified to exactly match the tested repository rules. No APK was built and no
physical-device test was performed during this change.

The first slice adds **Race** and **Training** to the walking screen's side menu.
It is a practice feature: one account-owned dog runs against Dash, Pip and Scout,
three clearly identified simulated partners. Walks, continuous steps, territory,
gacha and existing social features keep their current behavior.

## Stats and training

Every dog starts with Speed, Stamina and Acceleration at 40. One training session
costs 20 existing wallet points and adds 5 to one chosen stat, up to 100. The
training screen explains each stat and shows the server-confirmed wallet balance.
Signed-in players need a connection to load and train their dog. Signed-out
players can try a baseline practice race but cannot buy or save upgrades.

`DogStats` is the shared model for training and racing. A Firestore transaction
writes the point debit, upgraded dog and immutable training receipt together.
Insufficient points, a capped stat or a failed transaction cannot partially apply
an upgrade. A receipt ID is saved on the recording installation before training;
if acknowledgement is lost, Refresh reuses that ID. Native Firebase operations
can finish late; retries cannot charge that same training session twice.
Switching accounts cancels the displayed request and clears the previous dog's
stats and race. Each account's progress remains separate. Dog stats live in
Firebase and survive app restarts or signing in on another installation.

## Race behavior

Tap Start race and watch the four dog icons move automatically along a 200 m
track. Speed sets cruising pace; Acceleration sets how quickly it is reached;
Stamina delays fatigue. A small seeded variation adds race-to-race differences.
The race captures immutable copies of entrants' stats when it starts.

`DogRaceSimulation` has no Unity or Firebase dependency. It advances fixed 0.05 s
ticks regardless of rendering frame rate, calculates actual finish crossings
inside each tick, and sorts results by finish time. Closing or switching screens
ends the current practice race. A completed race offers Race again. Races have
no entry cost or reward and do not save competition results in this phase.

## Firebase contract

- `users/{uid}/dog/main`: schemaVersion 1, integer speed/stamina/acceleration,
  trainingCount, lastTrainingId and server updatedAt. Only the owner can read it.
  Initial creation permits only the baseline stats. Upgrades require an exact
  five-point change to one stat, an exact 20-point wallet debit, and a new receipt
  in the same atomic commit. Profiles cannot be freely reset or deleted.
- `users/{uid}/dogTrainingReceipts/{receiptId}`: stat, cost and server trainedAt.
  Receipt IDs are 32-character lowercase UUIDs. Only the owner can read them;
  creation requires a paired dog upgrade. Updating or deleting them is denied.
- Wallet earnings and milestone claim counts are preserved during training.
  The public leaderboard balance refreshes after an acknowledged purchase.

The training price/gain and starting stats are a versioned gameplay contract:
`DogStats` and `firestore.rules` must change together if these values change.
No Cloud Function or composite index is needed for this slice. Real-player races
will require a separate decision about sharing/public access to dog profiles;
the current private profile permissions do not silently expose players' stats.

## Unity setup and verification

Screens and controls are serialized in `StepCounterTestAmar.unity`, with ordinary
editable RectTransforms and TMP labels. `DogGameScreens` coordinates navigation,
account state and UI; the live WebView map is suspended while a dog screen is open
and restored on Back. Dog icons use a small scalable UI graphic with colored
collars. Replace that graphic with character artwork in a later phase.

`Walking Dog/Connect dog race and training screens` rebuilds only these screens
and their two menu buttons. This is an explicit editor command, not an automatic
import hook. `DogRacingSceneSetup.CapturePreview` renders training and race layouts
using temporary fixture data that is never uploaded to Firebase or saved to the
scene. Preview PNGs and validation logs are in ignored `Logs` directories.

Unity tests cover stat effects, reproducible results across frame rates/entrant
order, snapshots, paid training UI, insufficient funds, account changes and saved
navigation. Firebase emulator tests cover private baseline creation, atomic
training, duplicate receipts, forged upgrades, concurrent sessions and stat caps.
Phone checks still need a fresh client build: train with real points, restart,
check the same stats on another device, and confirm Back restores the walking map.

Future slices can add dog selection/artwork, training activities, strategy and
real player entrants without mixing those systems into this initial simulation.
