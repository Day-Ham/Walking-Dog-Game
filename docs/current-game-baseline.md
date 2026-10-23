# Current game documentation baseline

Reviewed October 19, 2026 on `Development-JM` at `cdc8ebc`.

## Latest integration

The local reflog records a fast-forward pull from `6d9296f` to `20d5cf2`, followed
by a fast-forward merge of `Development-Paul` to `cdc8ebc`. Review range:
`6d9296f..cdc8ebc`. These are three incoming commits:

- `20d5cf2`: reorganized animation assets and added Race/Training animations.
- `3845e5c`: script comments and cleanup, plus animated-close coordination.
- `cdc8ebc`: feature-screen background changes in `StepCounterTestAmar.unity`.

This integration does not introduce the existing multiplayer room feature;
that was already present at the review base. Most script changes are comments,
but the integration should not be described as exclusively cosmetic.

## Review notes

`DogGameScreens.Close` now cancels requests, stops the race and leaves the room
before starting an exit animation. Map restoration and screen deactivation wait
for completion. `DisableanimatedObject` sends `NotifyScreenDisabled` upwards
before disabling the target on the next frame. The new Race and Training close
clips call `setActiveFalse` at 0.5 seconds.

Verification gap: `TryPlayCloseAnimation` accepts an Animator/controller without
checking that the configured trigger exists or that a completion callback will
arrive. There is no close timeout. A missing event or incompatible controller
can leave `closing` set and the map suspended. Verify the serialized scene path
in Play Mode and on a phone; existing close tests do not exercise these clips.
This is a configuration-dependent risk, not a reproduced failure in the scene.

`git diff --check` reports trailing whitespace in incoming Unity assets and
metadata. No Unity compilation, Play Mode test, APK build, Firebase emulator
test or physical-device test was performed during this review. Earlier test
counts in feature documents describe their dated changes, not this revision.

## Game overview

Walking Dog is a Unity mobile walking game with continuous step rewards,
GPS territory walks, collectible collars, dog training, practice racing,
player race rooms and social features. The repository records Unity
`6000.5.10f1`. Enabled build scenes are `Title Screen` followed by
`StepCounterTestAmar`. Firebase-backed persistence and Android integrations
support the client; Firebase Functions use Node.js 22.

## Current feature references

| System | Documentation and current interpretation |
| --- | --- |
| Steps and points | [Continuous steps](continuous-steps.md): ten new steps earn one point, with remainder carryover. Main steps are account-scoped totals recorded on this installation, not a daily health total. |
| Legacy rewards | [Step points](step-points.md): per-walk rounding describes legacy walks; use continuous steps for current behavior. |
| Walking and recovery | [Tracking reliability](walk-tracking-reliability.md): GPS filtering, background route recording, interrupted-walk recovery and durable saves. |
| Territory | [Territory claiming](territory-claiming.md): capture geometry, ownership and synchronization. |
| Dog training | [Racing foundation](dog-racing-foundation.md): baseline stats 40, training costs 20 points for +5 to one stat, capped at 100. |
| Practice and player races | [Race lobby](race-lobby.md) supersedes the foundation document's practice-only scope: up to four players, shared roster/seed, simulated entrants in unused lanes, automatic 200 m races, no fees or rewards. |
| Rankings and friends | [Leaderboard integration](leaderboard-integration.md), [friends leaderboard](friends-leaderboard.md). |
| Social activity | [Following activity](following-activity.md): consult alongside its current service and UI scripts. |
| Authentication/backend | [Google sign-in](google-sign-in.md), [Firebase preparation](firebase-preparation.md), repository Firestore rules and Functions. |
| Collars and gacha | `GachaManager`, `CollarInventoryStore` and `CollarInventoryUI` implement selection, reward ownership and equipment; a dedicated current guide is still needed. |

The gameplay loop can be documented as walking to earn points, spending points
on dog training or collar pulls, equipping collected collars, and entering
practice races or shared race rooms. Territory walks and social rankings provide
additional progression and comparison.

## Documentation work to prioritize

1. Player guide: sign-in, permissions, main screen, walking, rewards, collection,
   training, room creation/joining, results and error recovery.
2. Gameplay reference: exact rewards, gacha probabilities/costs from the saved
   scene/catalog, milestones, territory eligibility and race stat effects.
3. Technical guide: scene navigation, tracking services, account ownership,
   local persistence, Firestore collections/rules and synchronization.
4. Setup and testing guide: Unity/Android dependencies, Firebase configuration,
   test commands and physical-device verification results.
5. Capture current screenshots after verifying navigation and animation on the
   reviewed build. Keep proposed features separate from observed behavior.

Before documenting release readiness, verify animated Back navigation restores
the map, account changes cancel stale operations, offline walking synchronizes
without duplicate rewards, and two phones show matching room race results.
