package com.walkingdog.profile;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.ImageDecoder;
import android.graphics.Matrix;
import android.media.ExifInterface;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.util.Base64;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import org.json.JSONObject;

/** The system grants access only to the selected image; no gallery permission. */
@androidx.annotation.Keep
public final class ProfilePhotoPicker extends Activity {
    private static int generation;
    private static String result = "";
    private int request;

    public static synchronized void begin(Activity activity) {
        cancel();
        int ticket = generation;
        activity.runOnUiThread(() -> {
            try { activity.startActivity(new Intent(activity, ProfilePhotoPicker.class).putExtra("request", ticket)); }
            catch (Exception error) { complete(ticket, "", "Could not open the photo picker."); }
        });
    }
    public static synchronized void cancel() { generation++; result = ""; }
    public static synchronized String takeResult() { String value = result; result = ""; return value; }
    private static synchronized void complete(int ticket, String photo, String error) {
        if (ticket != generation) return;
        try { result = new JSONObject().put("photo", photo).put("error", error).toString(); }
        catch (Exception ignored) { result = "{\"error\":\"Could not read this photo.\"}"; }
    }
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        request = getIntent().getIntExtra("request", -1);
        if (state != null) return;
        try {
            Intent pick = new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("image/*")
                .addCategory(Intent.CATEGORY_OPENABLE).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            startActivityForResult(pick, 1);
        } catch (Exception error) { complete(request, "", "No photo picker is available on this device."); finish(); }
    }
    @Override protected void onActivityResult(int code, int status, Intent data) {
        super.onActivityResult(code, status, data);
        if (code != 1) return;
        if (status != RESULT_OK || data == null || data.getData() == null) {
            complete(request, "", ""); finish(); return;
        }
        final Uri uri = data.getData();
        new Thread(() -> {
            Bitmap decoded = null, square = null, thumbnail = null;
            try {
                decoded = decode(uri);
                int edge = Math.min(decoded.getWidth(), decoded.getHeight());
                square = Bitmap.createBitmap(decoded, (decoded.getWidth() - edge) / 2, (decoded.getHeight() - edge) / 2, edge, edge);
                thumbnail = Bitmap.createScaledBitmap(square, 192, 192, true);
                ByteArrayOutputStream bytes = new ByteArrayOutputStream();
                for (int quality = 85; quality >= 35; quality -= 10) {
                    bytes.reset(); thumbnail.compress(Bitmap.CompressFormat.JPEG, quality, bytes);
                    if (bytes.size() <= 24000) break;
                }
                if (bytes.size() > 24000) throw new IllegalArgumentException("Photo is too large");
                complete(request, "data:image/jpeg;base64," + Base64.encodeToString(bytes.toByteArray(), Base64.NO_WRAP), "");
            } catch (Exception | OutOfMemoryError error) {
                complete(request, "", "Could not read this image. Try another photo.");
            } finally {
                if (thumbnail != null) thumbnail.recycle();
                if (square != null && square != thumbnail) square.recycle();
                if (decoded != null && decoded != square && decoded != thumbnail) decoded.recycle();
                runOnUiThread(this::finish);
            }
        }, "ProfileThumbnail").start();
    }
    private Bitmap decode(Uri uri) throws Exception {
        if (Build.VERSION.SDK_INT >= 28) {
            return ImageDecoder.decodeBitmap(ImageDecoder.createSource(getContentResolver(), uri), (decoder, info, source) -> {
                int width = info.getSize().getWidth(), height = info.getSize().getHeight();
                float scale = Math.min(1f, 384f / Math.max(width, height));
                decoder.setTargetSize(Math.max(1, Math.round(width * scale)), Math.max(1, Math.round(height * scale)));
                decoder.setAllocator(ImageDecoder.ALLOCATOR_SOFTWARE);
            });
        }
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inJustDecodeBounds = true;
        try (InputStream stream = getContentResolver().openInputStream(uri)) { BitmapFactory.decodeStream(stream, null, options); }
        if (options.outWidth <= 0 || options.outHeight <= 0) throw new IllegalArgumentException("Invalid image");
        options.inSampleSize = 1;
        while (Math.max(options.outWidth, options.outHeight) / options.inSampleSize > 768) options.inSampleSize *= 2;
        options.inJustDecodeBounds = false;
        Bitmap bitmap;
        try (InputStream stream = getContentResolver().openInputStream(uri)) { bitmap = BitmapFactory.decodeStream(stream, null, options); }
        if (bitmap == null) throw new IllegalArgumentException("Invalid image");
        try (InputStream stream = getContentResolver().openInputStream(uri)) {
            ExifInterface exif = new ExifInterface(stream);
            Matrix matrix = new Matrix();
            int orientation = exif.getAttributeInt(ExifInterface.TAG_ORIENTATION, 1);
            switch (orientation) {
                case 2: matrix.setScale(-1, 1); break;
                case 3: matrix.setRotate(180); break;
                case 4: matrix.setScale(1, -1); break;
                case 5: matrix.setRotate(90); matrix.postScale(-1, 1); break;
                case 6: matrix.setRotate(90); break;
                case 7: matrix.setRotate(270); matrix.postScale(-1, 1); break;
                case 8: matrix.setRotate(270); break;
                default: return bitmap;
            }
            Bitmap rotated = Bitmap.createBitmap(bitmap, 0, 0, bitmap.getWidth(), bitmap.getHeight(), matrix, true);
            if (rotated != bitmap) bitmap.recycle();
            return rotated;
        } catch (Exception ignored) { return bitmap; }
    }
}
