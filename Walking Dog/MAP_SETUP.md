# OpenFreeMap setup

This Unity project uses OpenFreeMap through MapLibre GL JS inside an Android WebView. This keeps the map provider free long-term: no Stadia trial, no Stadia API key, and no map tile account.

Official references:

- OpenFreeMap: https://openfreemap.org/
- MapLibre GL JS: https://maplibre.org/maplibre-gl-js/docs/

## Why OpenFreeMap

OpenFreeMap says its public instance is free, has no map-view/request limits, needs no registration, uses no API keys, allows commercial usage, and uses OpenStreetMap data. Attribution is still required.

The tradeoff is that OpenFreeMap is vector-tile based. Unity cannot render those tiles directly without a map renderer, so this project uses a WebView that runs MapLibre.

## Add the map to a Unity scene

1. Open the scene that should show the map.
2. Create or select a UI Panel area under a Canvas.
3. Add the `OpenFreeMapWebViewMap` component to that UI object.
4. Leave `Map Style` as `Liberty`, or choose `Positron` for a cleaner background.
5. Use zoom `16` or `17` for normal walking gameplay.

The component follows `StepCountAndGpsManager.Instance` automatically. In the Editor, it only shows a status message because Android WebView is available only in Android builds. In Android builds, it uses the fallback Manila coordinate until a real GPS fix exists.

## Required mobile permission

Android needs internet access to fetch MapLibre and OpenFreeMap assets. The custom Android manifest includes:

```xml
<uses-permission android:name="android.permission.INTERNET" />
```

Location permissions are already present for GPS tracking.

## Troubleshooting

If the map appears as a blank/white/gray area with a red dot, the Unity-to-WebView GPS sync is working. The red dot is our current-location marker. The missing part is the basemap, which means MapLibre could not draw OpenFreeMap tiles yet.

Check the small status label inside the map after rebuilding to Android:

- `Cannot reach OpenFreeMap` means the phone cannot reach `tiles.openfreemap.org`.
- `MapLibre CDN blocked or offline` means the phone cannot reach the MapLibre script on `unpkg.com`.
- `WebGL unavailable in Android WebView` means Android System WebView/Chrome needs updating, or the device does not support the required WebGL rendering.
- `Map still loading` usually means weak internet, blocked tile requests, or an Android WebView rendering issue.
- `GPS tracking (raster map)` means the app detected a blank vector map and switched to the simpler image-tile fallback.

The map is an Android native WebView overlay. That overlay always draws above Unity UI inside its rectangle, so keep the `OpenFreeMap Map Panel` away from step counters and buttons instead of trying to layer Unity text on top of it.

The WebView loads the HTML using a local `https://walkingdog.local/assets/` base URL instead of directly using `file:///android_asset`. This avoids Android WebView/MapLibre tile-worker origin issues where the page loads, the marker appears, but vector map tiles stay blank.

The raster fallback uses the public OpenStreetMap tile endpoint only for normal on-screen viewing. It is useful for prototype/demo reliability, but high-traffic production apps should use OpenFreeMap successfully, a paid/free-tier provider, or self-hosted tiles instead of depending on public OSM tile capacity.
