package com.gugarythm.player;

import android.app.Activity;
import android.app.Fragment;
import android.app.FragmentManager;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.provider.OpenableColumns;
import android.provider.DocumentsContract;
import android.database.Cursor;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;

public final class GugaFilePicker {
    static final int REQUEST_FILE = 6194;
    static final int REQUEST_FOLDER = 6195;
    private static final String PREFS = "gugarythm_import";
    private static final String RESULT = "result_path";

    public static void openFile(Activity activity) {
        withFragment(activity, true);
    }

    public static void openFolder(Activity activity) {
        withFragment(activity, false);
    }

    private static void withFragment(Activity activity, boolean file) {
        activity.runOnUiThread(() -> {
            FragmentManager manager = activity.getFragmentManager();
            GugaFilePickerFragment fragment = (GugaFilePickerFragment)manager.findFragmentByTag("GugaFilePickerFragment");
            if (fragment == null) {
                fragment = new GugaFilePickerFragment();
                manager.beginTransaction().add(fragment, "GugaFilePickerFragment").commitAllowingStateLoss();
                manager.executePendingTransactions();
            }
            if (file) fragment.openFile(); else fragment.openFolder();
        });
    }

    public static boolean handleActivityResult(Activity activity, int requestCode, int resultCode, Intent data) {
        if (requestCode != REQUEST_FILE && requestCode != REQUEST_FOLDER) return false;
        if (resultCode != Activity.RESULT_OK || data == null || data.getData() == null) return true;
        Uri uri = data.getData();
        try {
            File root = new File(activity.getCacheDir(), "GugarythmImports");
            if (!root.exists() && !root.mkdirs()) throw new IllegalStateException("Cannot create import cache");
            File target;
            if (requestCode == REQUEST_FOLDER) {
                target = new File(root, "folder-" + System.currentTimeMillis());
                if (!target.mkdirs()) throw new IllegalStateException("Cannot create folder import cache");
                copyTree(activity, uri, DocumentsContract.getTreeDocumentId(uri), target, new long[] { 0 }, new int[] { 0 });
            } else {
                target = new File(root, sanitize(displayName(activity, uri)));
                copyFile(activity, uri, target, new long[] { 0 });
            }
            activity.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(RESULT, target.getAbsolutePath()).apply();
        } catch (Exception exception) {
            activity.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(RESULT, "ERROR:" + exception.getMessage()).apply();
        }
        return true;
    }

    private static void copyTree(Context context, Uri treeUri, String documentId, File target, long[] total, int[] count) throws Exception {
        Uri children = DocumentsContract.buildChildDocumentsUriUsingTree(treeUri, documentId);
        String[] projection = new String[] {
            DocumentsContract.Document.COLUMN_DOCUMENT_ID,
            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_MIME_TYPE
        };
        try (Cursor cursor = context.getContentResolver().query(children, projection, null, null, null)) {
            if (cursor == null) throw new IllegalStateException("Cannot read selected folder");
            while (cursor.moveToNext()) {
                if (++count[0] > 512) throw new IllegalStateException("Folder contains more than 512 entries");
                String childId = cursor.getString(0);
                String name = sanitize(cursor.getString(1));
                String mime = cursor.getString(2);
                Uri childUri = DocumentsContract.buildDocumentUriUsingTree(treeUri, childId);
                File child = new File(target, name);
                if (DocumentsContract.Document.MIME_TYPE_DIR.equals(mime)) {
                    if (!child.mkdirs()) throw new IllegalStateException("Cannot create cached folder: " + name);
                    copyTree(context, treeUri, childId, child, total, count);
                } else copyFile(context, childUri, child, total);
            }
        }
    }

    private static void copyFile(Context context, Uri uri, File target, long[] total) throws Exception {
        try (InputStream input = context.getContentResolver().openInputStream(uri);
             FileOutputStream output = new FileOutputStream(target)) {
            if (input == null) throw new IllegalStateException("Cannot open selected document");
            byte[] buffer = new byte[65536];
            int read;
            while ((read = input.read(buffer)) >= 0) {
                total[0] += read;
                if (total[0] > 256L * 1024L * 1024L) throw new IllegalStateException("Selected content is larger than 256 MiB");
                output.write(buffer, 0, read);
            }
        }
    }

    public static String consumeResult(Activity activity) {
        String value = activity.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getString(RESULT, "");
        if (!value.isEmpty()) activity.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().remove(RESULT).apply();
        return value;
    }

    private static String displayName(Context context, Uri uri) {
        String result = null;
        try (Cursor cursor = context.getContentResolver().query(uri, null, null, null, null)) {
            if (cursor != null && cursor.moveToFirst()) {
                int index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                if (index >= 0) result = cursor.getString(index);
            }
        }
        if (result == null || result.isEmpty()) result = "import.chart";
        return result;
    }

    private static String sanitize(String value) {
        String clean = value.replaceAll("[^A-Za-z0-9._() -]", "_");
        return clean.isEmpty() ? "import.chart" : clean;
    }
}
