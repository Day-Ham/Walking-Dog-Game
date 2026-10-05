package com.walkingdog.tracking;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.content.pm.ServiceInfo;
import android.location.*;
import android.os.*;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;

/** Started by a visible Start/Resume Walk action, stopped when the walk finishes.
 * No background-location permission or automatic restart after a force-stop. */
public final class WalkLocationService extends Service implements LocationListener {
    private static final Object LOCK = new Object();
    private static final String CHANNEL = "walking_dog_walk";
    
    private static final String ALERT_CHANNEL = "walking_dog_alerts"; // notification alert
    
    private static final int NOTIFICATION = 4102; // code for notification default
    private static final int TERRITORY_ALERT_NOTIFICATION = 4103; // notification code for territory
    private static final int FREE_ROLL_NOTIFICATION = 4104; // notification code for free roll

  //// modify these according to the minimum
    private static final float MINIMUM_TERRITORY_DISTANCE_METERS = 200f;
    private static final float TERRITORY_CLOSURE_METERS = 30f;
  
  
    private static final ArrayList<JSONObject> records = new ArrayList<>();
    private static WalkLocationService instance;
    private static String activeId = "", loadedPath = "", error = "";
    private static boolean accepting, starting;
    private static long sequence;
    private LocationManager locations;
    private FileOutputStream journal;

    // Territory parameters for notification in local android 
    private boolean territoryAlertEnabled;
    private boolean territoryAlertSent;
    private double territoryStartLatitude, territoryStartLongitude;
    private float territoryDistanceMeters;
    private Location lastTerritoryLocation;

    public static String begin(Activity activity, String directory, String id, boolean canClaimTerritory,
        float startLatitude, float startLongitude, float distanceAlreadyWalked) {
        synchronized (LOCK) {
            try {
                if (activity.checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION) != PackageManager.PERMISSION_GRANTED)
                    return "Precise location permission is required";
                if ((accepting || starting) && !activeId.equals(id)) return "Another walk is still recording";
                load(directory, id);
                activeId = id;
                error = "";
                starting = true;
                Intent intent = new Intent(activity, WalkLocationService.class);
                intent.putExtra("directory", directory);
                intent.putExtra("id", id);
                intent.putExtra("canClaimTerritory", canClaimTerritory);
                intent.putExtra("startLatitude", startLatitude);
                intent.putExtra("startLongitude", startLongitude);
                intent.putExtra("distanceAlreadyWalked", distanceAlreadyWalked);
                if (Build.VERSION.SDK_INT >= 26) activity.startForegroundService(intent);
                else activity.startService(intent);
                return "";
            } catch (Exception e) { starting = false; error = "Could not start screen-off tracking"; return error; }
        }
    }

    /**
     * Shows a free-roll notification created entirely on this phone.
     * Unity calls this after its wallet sees a newly reached step milestone. Returning false
     * tells Unity not to record the milestone as alerted when Android permission is missing.
     */
    public static boolean notifyFreeRoll(Activity activity, long availableRolls) {
        if (activity == null || availableRolls <= 0) return false;
        if (Build.VERSION.SDK_INT >= 33 && activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)
            != PackageManager.PERMISSION_GRANTED) return false;
        try {
            NotificationManager notifications = (NotificationManager)activity.getSystemService(NOTIFICATION_SERVICE);
            createAlertChannel(notifications);
            Intent launch = activity.getPackageManager().getLaunchIntentForPackage(activity.getPackageName());
            PendingIntent open = PendingIntent.getActivity(activity, FREE_ROLL_NOTIFICATION, launch,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
            String rollText = availableRolls == 1 ? "1 free roll is ready." : availableRolls + " free rolls are ready.";
            Notification.Builder builder = Build.VERSION.SDK_INT >= 26
                ? new Notification.Builder(activity, ALERT_CHANNEL) : new Notification.Builder(activity);
            notifications.notify(FREE_ROLL_NOTIFICATION, builder.setContentTitle("Free roll available!")
                .setContentText(rollText).setSmallIcon(android.R.drawable.ic_dialog_info)
                .setAutoCancel(true).setContentIntent(open).build());
            return true;
        } catch (Exception ignored) { return false; }
    }

    public static void finish(String id) {
        synchronized (LOCK) {
            if (!activeId.equals(id)) return;
            accepting = starting = false;
            if (instance != null) {
                WalkLocationService stopping = instance;
                stopping.closeSensorsAndJournal();
                instance = null;
                stopping.stopForeground(true);
                stopping.stopSelf();
            }
        }
    }

    public static String read(String directory, String id, long afterSequence) throws Exception {
        synchronized (LOCK) {
            load(directory, id);
            JSONArray samples = new JSONArray();
            // Sequence numbers are contiguous, including explicit interruption records.
            int index = (int)Math.min(records.size(), Math.max(0, afterSequence));
            while (index < records.size() && samples.length() < 512) samples.put(records.get(index++));
            return new JSONObject().put("running", activeId.equals(id) && (accepting || starting))
                .put("error", error).put("samples", samples).toString();
        }
    }

    private static void load(String directory, String id) throws Exception {
        if (id == null || !id.matches("[A-Za-z0-9_-]{1,128}")) throw new IOException("Invalid walk ID");
        File folder = new File(directory);
        File file = new File(folder, "route_" + id + ".jsonl");
        String path = file.getCanonicalPath();
        if (path.equals(loadedPath)) return;
        if (accepting || starting) throw new IOException("Another journal is active");
        records.clear(); sequence = 0;
        if (file.exists()) {
            // Truncate only an incomplete trailing write before appending again.
            try (RandomAccessFile input = new RandomAccessFile(file, "rw")) {
                long validEnd = 0;
                String line;
                while ((line = input.readLine()) != null) {
                    try {
                        JSONObject value = new JSONObject(line);
                        if (value.getLong("sequence") != sequence + 1) break;
                        records.add(value); sequence++; validEnd = input.getFilePointer();
                    } catch (JSONException badTail) { break; }
                }
                if (validEnd < input.length()) input.setLength(validEnd);
            }
        }
        loadedPath = path;
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        synchronized (LOCK) {
            if (intent == null || !starting || !activeId.equals(intent.getStringExtra("id"))) {
                stopSelf(startId); return START_NOT_STICKY;
            }
            try {
                NotificationManager notifications = (NotificationManager)getSystemService(NOTIFICATION_SERVICE);
                if (Build.VERSION.SDK_INT >= 26) notifications.createNotificationChannel(
                    new NotificationChannel(CHANNEL, "Dog walks", NotificationManager.IMPORTANCE_LOW));
                Intent launch = getPackageManager().getLaunchIntentForPackage(getPackageName());
                PendingIntent open = PendingIntent.getActivity(this, 0, launch,
                    PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
                Notification.Builder builder = Build.VERSION.SDK_INT >= 26
                    ? new Notification.Builder(this, CHANNEL) : new Notification.Builder(this);
                Notification notification = builder.setContentTitle("Dog walk in progress")
                    .setContentText("Recording your route. Open the app to finish your walk.")
                    .setSmallIcon(android.R.drawable.ic_menu_mylocation).setOngoing(true).setContentIntent(open).build();
                if (Build.VERSION.SDK_INT >= 29) startForeground(NOTIFICATION, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_LOCATION);
                else startForeground(NOTIFICATION, notification);
                if (instance == this && accepting) { starting = false; return START_NOT_STICKY; }
                File folder = new File(intent.getStringExtra("directory"));
                if (!folder.exists() && !folder.mkdirs()) throw new IOException("Cannot create journal");
                journal = new FileOutputStream(loadedPath, true);
                instance = this; accepting = true; starting = false;
               
               // territory function notif
                territoryAlertEnabled = intent.getBooleanExtra("canClaimTerritory", false);
                territoryStartLatitude = intent.getFloatExtra("startLatitude", 0f);
                territoryStartLongitude = intent.getFloatExtra("startLongitude", 0f);
                territoryDistanceMeters = Math.max(0f, intent.getFloatExtra("distanceAlreadyWalked", 0f));
                territoryAlertSent = false;
                lastTerritoryLocation = null;
                locations = (LocationManager)getSystemService(LOCATION_SERVICE);
                // Zero minimum distance keeps fresh timestamps even while the dog stops.
                locations.requestLocationUpdates(LocationManager.GPS_PROVIDER, 1500, 0, this, Looper.getMainLooper());
                if (!locations.isProviderEnabled(LocationManager.GPS_PROVIDER)) onProviderDisabled(LocationManager.GPS_PROVIDER);
            } catch (Exception e) {
                error = "Screen-off tracking unavailable. Keep the app open";
                accepting = starting = false;
                stopSelf();
            }
            return START_NOT_STICKY;
        }
    }

    @Override public void onLocationChanged(Location location) {
        synchronized (LOCK) {
            if (!accepting) return;
            try {
                // Use Android's monotonic location age to avoid accepting cached fixes.
                double observed = System.currentTimeMillis() / 1000d;
                double age = (SystemClock.elapsedRealtimeNanos() - location.getElapsedRealtimeNanos()) / 1e9;
                JSONObject sample = new JSONObject().put("latitude", location.getLatitude())
                    .put("longitude", location.getLongitude()).put("accuracy", location.hasAccuracy() ? location.getAccuracy() : 0)
                    .put("timestamp", observed - age).put("observedAt", observed);
                append(sample);
                checkTerritoryClosure(location);
            } catch (Exception e) { journalFailure(); }
        }
    }

    private void append(JSONObject sample) throws Exception {
        if (records.size() >= 50000) throw new IOException("Walk journal is full");
        sample.put("sequence", sequence + 1);
        byte[] bytes = (sample.toString() + "\n").getBytes(StandardCharsets.UTF_8);
        journal.write(bytes);
        journal.getFD().sync();
        records.add(sample); sequence++;
    }

    /**
     * Watches the active walk locally while Unity may be paused. Once the player has travelled
     * at least 200 m and enters the 50 m range around the route start, it posts one notification.
     * This is only a helpful cue; Unity still performs the final area and route validation.
     */
    private void checkTerritoryClosure(Location location) {
        if (!territoryAlertEnabled || territoryAlertSent || !location.hasAccuracy() || location.getAccuracy() > 10f) return;
        if (lastTerritoryLocation != null) {
            float legMeters = lastTerritoryLocation.distanceTo(location);
            // Ignore an implausible GPS jump so it cannot falsely qualify the 200 m requirement.
            long elapsedMilliseconds = Math.max(1L, location.getElapsedRealtimeNanos() - lastTerritoryLocation.getElapsedRealtimeNanos()) / 1000000L;
            if (legMeters <= 6f * (elapsedMilliseconds / 1000f) + 5f) territoryDistanceMeters += legMeters;
        }
        lastTerritoryLocation = new Location(location);
        if (territoryDistanceMeters < MINIMUM_TERRITORY_DISTANCE_METERS) return;

        float[] result = new float[1];
        Location.distanceBetween(location.getLatitude(), location.getLongitude(), territoryStartLatitude,
            territoryStartLongitude, result);
        if (result[0] > TERRITORY_CLOSURE_METERS) return;

        territoryAlertSent = showTerritoryClosureNotification(Math.round(result[0]));
    }

    /** Shows the one-time local alert used when an active walk returns close enough to its start. */
    private boolean showTerritoryClosureNotification(int distanceToStartMeters) {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)
            != PackageManager.PERMISSION_GRANTED) return false;
        NotificationManager notifications = (NotificationManager)getSystemService(NOTIFICATION_SERVICE);
        createAlertChannel(notifications);
        Intent launch = getPackageManager().getLaunchIntentForPackage(getPackageName());
        PendingIntent open = PendingIntent.getActivity(this, TERRITORY_ALERT_NOTIFICATION, launch,
            PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        String text = "You are " + distanceToStartMeters + " m from your start. End the walk to validate your territory.";
        Notification.Builder builder = Build.VERSION.SDK_INT >= 26
            ? new Notification.Builder(this, ALERT_CHANNEL) : new Notification.Builder(this);
        notifications.notify(TERRITORY_ALERT_NOTIFICATION, builder.setContentTitle("Territory closing range reached")
            .setContentText(text).setSmallIcon(android.R.drawable.ic_menu_mylocation)
            .setAutoCancel(true).setContentIntent(open).build());
        return true;
    }

    /** Creates the visible alert channel once; the existing low-priority channel remains for tracking. */
    private static void createAlertChannel(NotificationManager notifications) {
        if (Build.VERSION.SDK_INT >= 26) notifications.createNotificationChannel(
            new NotificationChannel(ALERT_CHANNEL, "Walk alerts", NotificationManager.IMPORTANCE_DEFAULT));
    }

    private void journalFailure() {
        error = "Route recovery storage unavailable. Keep the app open";
        accepting = starting = false;
        closeSensorsAndJournal();
        loadedPath = ""; // Reload and repair a partial trailing write before any future append.
        stopSelf();
    }

    @Override public void onProviderDisabled(String provider) {
        synchronized (LOCK) {
            if (!accepting) return;
            try { append(new JSONObject().put("interrupted", true).put("observedAt", System.currentTimeMillis() / 1000d)); }
            catch (Exception e) { journalFailure(); }
        }
    }
    @Override public void onProviderEnabled(String provider) { }
    @Override public void onStatusChanged(String provider, int status, Bundle extras) { }
    @Override public IBinder onBind(Intent intent) { return null; }
    private void closeSensorsAndJournal() {
        if (locations != null) { locations.removeUpdates(this); locations = null; }
        try { if (journal != null) journal.close(); } catch (IOException ignored) { }
        journal = null;
    }
    @Override public void onDestroy() {
        synchronized (LOCK) {
            closeSensorsAndJournal();
            if (instance == this) { instance = null; accepting = starting = false; }
            stopForeground(true);
        }
        super.onDestroy();
    }
}
