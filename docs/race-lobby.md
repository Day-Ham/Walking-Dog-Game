# Race room testing and rollout

October 15, 2026: fixed Create Room and Join Room while preserving the current
screens and race behavior. The deployed Firestore rules previously had no
permission for `raceRooms`, and lobby rendering erased request errors.

## Current behavior

- Signed-in players create a room and share its five-character code. Up to four
  players can join; simulated dogs fill unused lanes when the host starts.
- Creating or joining reads the player's saved dog stats, equipped inventory
  item and profile nickname. Room stats are snapshots, not editable upgrades.
- Only the host can start. Every client uses the same frozen roster and seed
  for the automatic 200 m simulation. There are no race fees or rewards.
- Guests can leave a waiting room; the host leaving closes it. Rooms cannot
  be listed publicly. Knowing a code allows a signed-in player to read it.
- Failed requests retain an explanatory message. Requests time out after
  30 seconds and restore controls. Closing a screen, switching accounts or
  destroying the component discards stale callbacks and stops listeners.

The client preserves room creation metadata on joins and leaves. Firebase
validates entrants against their saved dog and inventory, permits changes only
to the caller's slot, and freezes the roster at race start. Concurrent joins
cannot admit more than four players.

## Verification

- Unity EditMode: 142/142 passed, including timeout, late completion, account
  switching, listener disposal, visible errors and controls after race start.
- Firebase emulator: 61/61 passed, including concurrent joins, ownership,
  saved stats/equipment, host-only start and terminal room closure.
- Map route and territory script checks passed.
- Firestore rules deployed to `walky-aa25c`; release
  `7e8a5ec9-9589-4a04-b6fa-0258915ccd17` was read back and verified to match
  the tested rules exactly. No Cloud Function or index deployment was needed.
- The leaderboard screen is now inactive in the saved startup scene so the
  walking map is visible on launch. Existing redesigned layouts are retained.

## Phone test

Build and install the updated client on every participating phone. Older
clients replace the room document during a join and omit creation metadata,
which the new rules correctly reject.

1. Sign in on two phones, open Race and create a room on the first.
2. Enter its code on the second. Confirm both players and their saved stats
   appear on both phones, then start from the host.
3. Confirm both phones show the same finishing order. Try Race again and
   Training afterwards to confirm controls remain available.
4. Create another room, leave as guest, rejoin, then leave as host. Confirm
   the guest sees that the room closed and can create another room.
5. Check Back restores the map, and test loss of connection to confirm a
   visible error or timeout appears.

No APK was built and no physical two-phone test was performed in this fix.
Host disconnection without leaving has no automatic room expiry in this phase.
