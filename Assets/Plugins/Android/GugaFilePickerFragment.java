package com.gugarhythm.player;

import android.app.Fragment;
import android.content.Intent;
import android.os.Bundle;

public final class GugaFilePickerFragment extends Fragment {
    public void openFile() {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("application/octet-stream");
        intent.putExtra(Intent.EXTRA_MIME_TYPES, new String[] {
            "application/octet-stream", "application/zip"
        });
        intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
        startActivityForResult(intent, GugaFilePicker.REQUEST_FILE);
    }

    public void openFolder() {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
        startActivityForResult(intent, GugaFilePicker.REQUEST_FOLDER);
    }

    @Override
    public void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (!GugaFilePicker.handleActivityResult(getActivity(), requestCode, resultCode, data)) {
            super.onActivityResult(requestCode, resultCode, data);
        }
    }
}
