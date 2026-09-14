# Personal territory claiming, version 1

New walks started while signed in can claim tiles after Stop Walk successfully saves the completed walk. The live and history maps show the current player's green owned tiles beneath the orange route. The summary reports newly claimed tiles, already owned tiles, or a specific reason for rejection. Ordinary walks still save even when they do not qualify.

## Rules

- Record a continuous GPS route with accuracy of 10 metres or better. Missing segments, invalid timestamps, and implausible jumps invalidate the claim.
- Walk at least 200 metres. Finish within 25 metres of the first recorded location.
- Enclose at least 2,500 square metres with a simple loop. Crossing, touching, or retracing non-adjacent route edges is rejected; this first version does not split complex routes into smaller loops.
- Claim each grid tile whose centre lies inside the closed route. A tile can extend slightly beyond the walked boundary.
- The stable grid uses 50-metre Web Mercator cells, approximately 48 metres wide around Manila. Ground dimensions shrink at higher latitudes. This is a gameplay grid, not property ownership or a cadastral map.
- Processing is bounded to 6,000 GPS samples, 5 km projected width/height, 10,000 candidate cells and 1,000 enclosed tiles per walk. Latitudes beyond ±75° and routes crossing the date line are unsupported.

## Persistence and account behavior

`SavedWalkSession.territoryVersion` marks walks begun with this feature while signed in. Legacy walks, unsigned walks later imported into an account, and unfinished checkpoints are not automatically awarded. Recovery preserves the marker, but a recovered route with tracking gaps cannot claim.

`TerritoryService` processes completed local walks after saving and on account initialization. It retries failed territory writes every 30 seconds. A single per-owner ledger atomically commits both new ownership and the processed walk ID. Repeated saves return the original receipt. Overlapping walks only add unowned tiles. After a crash between walk and territory commits, startup replays missing claims from saved walks.

Ledgers live under `Application.persistentDataPath/WalkSessions/Territories`, with filenames derived from SHA-256 of the authenticated UID. Atomic replacement preserves a backup; corrupt primary files can recover the previous ledger and replay missing saved walks. If both copies are unreadable, or a newer ledger version exists, the app preserves them and reports territory saving as pending. It does not reset ownership silently. Signing out or switching accounts clears the previous player's map overlay.

Territories are local to this device. Firebase still uploads only the existing walk summary fields. Territory ownership does not sync across devices, compete with other players, expire, or award extra XP/coins. Clearing app data removes local territories. Server-authoritative claims and spatial queries are future work; the prototype sends the owner's compact tile list to the map and caches its geometry by revision.

## Validation

- Unity EditMode: `TerritoryTests` covers simple/concave loops, closure, crossings/retracing, minimum sizes, gaps, invalid GPS, size limits, stable tiles, repeated/overlapping claims, owner isolation, backup recovery, write failure retries, startup replay, sign-out and the Stop Walk integration. Existing tracking, persistence and Firebase adapter tests remain in the suite.
- `node scripts/test-map-routes.cjs` checks route breaks plus territory projection, cached geometry, vector/raster rendering, style reload and account clearing.
- `node scripts/preview-territory-map.cjs` creates a synthetic map preview in the ignored `Walking Dog/Logs` directory. Open it with `?raster` to exercise the fallback.
- Unity editor entry point `WalkTrackingValidation.CaptureDialogs` renders summary and recovery previews, including successful and pending territory messages.
- `WalkTrackingValidation.BuildTerritoryAndroid` creates `Walking Dog/Builds/WalkingDog-territories.apk`.

## Phone acceptance check

Install the new APK over the current app, sign in, wait for accurate GPS, then walk a simple loop around an accessible area. Walk at least 200 m, enclose at least 2,500 m², return within 25 m of the starting fix, and stop. Expect a claim receipt and green tiles on the map. Repeat the loop: it should report already owned tiles. Reopen the app to check persistence. An open route or a route with GPS gaps should save normally with an explanation and no claim. The previous tracking version's screen-lock test passed on the user's Android phone; this new territory build still needs that field acceptance check.
