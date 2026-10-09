# Trail and loop territory claiming (version 2)

A qualifying completed walk claims a corridor along its recorded GPS trail. A qualifying closed loop also claims its enclosed area. The green boundary follows the route instead of snapping to grid cells. Live and history maps show the current player's combined ownership, and the summary reports newly claimed square metres.

## Rules and geometry

- Start a new walk while signed in; save it with Stop Walk.
- Record a continuous GPS route with accuracy of 10 metres or better, valid increasing timestamps, and no implausible jumps.
- Walk at least 50 metres to claim the trail corridor, buffered by 5 projected metres on each side. Actual ground width varies with latitude because the buffer uses Web Mercator coordinates.
- To also claim a loop's interior, walk at least 200 metres, enclose at least 2,500 m², and finish within 30 metres of the first recorded location. The short closing gap is connected by a straight boundary. The HUD shows trail progress first, then the optional loop-closing guidance.
- Crossing, touching or retracing non-adjacent edges prevents filling the loop's interior; an otherwise valid trail still claims its corridor. Complex figure-eight routes are not split into multiple filled loops.
- Claims use the full saved route, before display downsampling. Geometry operations use centimetre-rounded Web Mercator coordinates. Displayed area is an approximate geographic surface area, adjusted for latitude.
- Clipper2 2.0.1 performs polygon union and difference. Overlapping land counts once, adjacent claims merge, and unclaimed holes remain empty in both vector and raster rendering.
- Processing is bounded to 6,000 GPS samples. Loop filling additionally checks 5 km projected width/height and 2.5 million projected square metres; these bounds do not suppress the trail corridor. Legacy tile evaluation also limits the bounding box to 10,000 cells. Latitudes beyond ±75° and date-line crossings are unsupported.

Clipper2 source and its Boost Software License are vendored under `Assets/Scripts/ThirdParty/Clipper2`. Official source: https://github.com/AngusJohnson/Clipper2/tree/Clipper2_2.0.1/CSharp/Clipper2Lib

## Existing claims and persistence

Version 1 tile ledgers automatically upgrade atomically to version 2 polygon ledgers. If an accepted tile claim still has its locally saved route, the entire validated loop becomes the claim's shape. If the route is unavailable, its existing tile footprint is preserved as a polygon. Previously rejected claims stay rejected. Upgrade retains a backup and does not reset ownership.

Each player's ledger stores processed walk IDs, newly added polygon regions, and area receipts together. A retry returns the original receipt without adding ownership. A crash between saving the walk and committing territory is recovered by replaying eligible completed local walks at startup. Failed writes retry every 30 seconds. Signing out or changing accounts clears the previous player's overlay.

New walks use `SavedWalkSession.territoryVersion = 2`. Pending version 1 walks remain eligible; walks predating territory support and unsigned walks later imported into an account do not receive automatic claims. Recovered walks retain their version marker, but tracking gaps invalidate the loop.

Ledgers remain under `Application.persistentDataPath/WalkSessions/Territories`, with owner filenames hashed using SHA-256. Corrupt primary files can recover a backup and replay saved walks. If both copies fail validation or the format is newer than this app supports, files are preserved and the UI reports territory saving as pending.

Territories are personal and local to this device. Firebase uploads only the existing walk summary fields. No multiplayer ownership, territory cloud sync, expiration, or extra XP/coins are included. Clearing app data removes local territories.

## Validation and build

- Unity EditMode tests cover loop geometry, non-grid boundaries, union/difference, holes, overlaps, migration with/without saved routes, duplicate/restarted saves, account isolation, backup recovery, failed commits, and Stop Walk integration, alongside existing tracking/cloud tests.
- `node scripts/test-map-routes.cjs` checks route breaks, polygon projection, caching, vector/raster holes, account clearing, style reload, and compatibility with the earlier loop-preview helper.
- `node scripts/preview-territory-map.cjs` creates a synthetic map fixture in the ignored `Walking Dog/Logs` folder. Add `?raster` when opening it manually to exercise the fallback. Automated browser access to the local preview was blocked by browser URL policy.
- `WalkTrackingValidation.CaptureDialogs` renders success and pending summary examples.
- `WalkTrackingValidation.BuildTerritoryAndroid` creates `Walking Dog/Builds/WalkingDog-loop-territories.apk`.

## Phone check

Install the new build over the existing app, keeping its data. Sign in and check that existing claims remain visible. Record an open trail of at least 50 m and verify its corridor is claimed. Then record a fresh simple loop meeting the distance/area/GPS rules. After Stop Walk, expect a filled loop plus its trail corridor and a receipt in m². Repeating or partly overlapping a route should only add previously unowned area. Reopen the app to check persistence. GPS gaps still save the walk without a territory award.
