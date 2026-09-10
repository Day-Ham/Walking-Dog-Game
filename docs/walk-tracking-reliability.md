# Walking reliability and territory prerequisites

Implemented September 10, 2026. This adds the recording foundation for territory
claims; it does not award land or change the cloud walk-summary payload.

## Player behavior

- Start Walk becomes available once a precise, fresh GPS fix is available.
- During a walk, the three existing status rows show steps/distance/time,
  screen-off route recording availability, and GPS/recovery status.
- An Android location foreground service starts from Start/Resume Walk. It records
  location updates while Unity is paused and posts an ongoing walk notification.
  Stop Walk stops the service and drains its remaining samples before saving.
- GPS outages, inaccurate readings, rejected jumps and recovered sessions leave
  explicit breaks. Neither distance calculation nor either map renderer joins
  those breaks. Walking can continue after an interruption.
- Unfinished walks offer Resume walk or Save and finish. Recovery is scoped to the
  original signed-in account (or the signed-out local owner), and retains the walk
  ID and saved steps. Recovery does not reassign ownership.
- Finishing opens a summary with saved state and route quality. A failed final
  save offers Retry save and prevents replacing the unsaved walk with a new one.

## Recording rules

`WalkGpsFilter` checks finite values, geographic bounds, positive accuracy no worse
than 10 metres, timestamps no older than 10 seconds at acquisition, and at most
2 seconds into the future. Duplicate/out-of-order fixes are ignored. A gap greater
than 15 seconds between good fixes starts a new segment. Movement greater than
6 m/s plus a 5 m noise allowance is rejected and breaks continuity. Small movement
below max(3 m, 0.75 × accuracy) does not add distance. Fresh stationary fixes still
update freshness, so a normal stop does not by itself count as a GPS outage.

These are initial outdoor-test parameters, not proof against GPS spoofing.
Territory logic must use the full recorded samples and their segment metadata,
not the reduced set of points sent to the map. `trackingVersion == 0` identifies
legacy routes without continuity metadata. `trackingVersion == 1` adds
`hasTrackingGaps`, point `startsNewSegment`, GPS timestamps and a native journal
sequence. Cloud summaries still exclude coordinates and tracking metadata.

## Durability and Android lifecycle

Completed saves retain their existing immutable behavior. Mutable recovery
snapshots use the same atomic-write/backup mechanism in `WalkSessions/Active`,
outside the completed-walk upload queue. Snapshots are saved at start, every
15 seconds, pause, quit and before completion. A process crash can lose up to the
last checkpoint interval of foreground-only state. A completed file takes priority
over a leftover checkpoint if the app stops between final commit and cleanup.

The native service appends acquired samples to a private `WalkSessions/Background`
JSON-lines journal and flushes each record to disk. The sequence is checkpointed
with the accepted route, so replay does not duplicate points. On recovery, samples
recorded after the checkpoint can be replayed before a new segment starts.
Unrecorded time while the app/service was stopped is excluded from resumed duration.
An incomplete trailing journal write is discarded before appending again.

The service is a location foreground service started while the activity is visible;
it does not request always-on background location, track outside an active walk,
or restart automatically after a force-stop. Swiping away the app stops the service.
Android can still stop the process; the saved walk then needs explicit recovery.
Permission/service/storage failures fall back to foreground tracking with an
on-screen explanation. The native journal is bounded to 50,000 acquired samples;
reaching that limit also stops the service and reports the fallback.

The existing step sensor implementation remains in use. Its screen-off behavior,
hardware-counter reconciliation, battery behavior and manufacturer power-saving
restrictions require device testing; the new native service records GPS samples.

## Verification

- Unity EditMode: 36 tests passed, including 11 new tracking/recovery tests.
- Android-conditioned C# compiled against the installed Unity/Firebase references.
- Native Java compiled against Android API 36.
- Final Android development APK built successfully through Unity, IL2CPP and Gradle.
  The packaged manifest was inspected for the location service, service type and
  notification/foreground-service permissions. Build log: `Logs/walk-android-build-final.log`.
- `node scripts/test-map-routes.cjs` verifies script syntax, route gaps, isolated
  points, legacy routes and the raster fallback.
- Recovery and summary dialogs were rendered at 720 × 1280 and visually inspected.
  Preview PNGs and validation logs are in the ignored Unity `Logs` directory.

Editor batch helpers in `WalkTrackingValidation` can render the dialogs or build
the enabled Android scenes. The development APK output is
`Walking Dog/Builds/WalkingDog-tracking.apk` (built successfully for this change).

No Android device was connected during this work. Before enabling territory claims:

1. Log in on a phone, wait for GPS ready, walk a small loop and finish. Inspect the
   route, step/distance/time summary and synced history; reopen the app to verify it.
2. Stand still for a minute: check that distance barely changes and GPS stays fresh.
3. Lock the screen for several minutes during a walk, then reopen it. Verify the
   route follows the actual path, step totals reconcile and the notification ends
   when Stop Walk is pressed.
4. Turn location off mid-walk, move, then turn it back on. Check for a visible break
   and verify the missing stretch does not add distance.
5. Interrupt the process during a walk. Reopen with the same account, recover, walk
   farther and finish. Verify one completed walk, retained earlier steps and a break
   at the recovery boundary. Check that another account cannot recover that draft.
6. Finish offline, reconnect and verify one cloud summary with the original walk ID.
   Also verify GPS permission denial and a failed/retried save are understandable.

Android implementation references:
[foreground service types](https://developer.android.com/develop/background-work/services/fgs/service-types),
[foreground service start restrictions](https://developer.android.com/develop/background-work/services/fgs/restrictions-bg-start),
[Unity Java source plug-ins](https://docs.unity3d.com/6000.0/Documentation/Manual/AndroidJavaSourcePlugins.html).
