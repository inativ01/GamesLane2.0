package com.gameslane.app;

import android.app.UiModeManager;
import android.content.Context;
import android.content.res.Configuration;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import androidx.appcompat.app.AppCompatDelegate;
import com.getcapacitor.BridgeActivity;

/**
 * The app's screen: Capacitor's web view, with the GamesLane plugin. On Google TV and Android TV, it shows the web TV
 * app ({@code /tv.html}, named after the TV) instead of the site's main screen.
 */
public class MainActivity extends BridgeActivity {

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        registerPlugin(GamesLanePlugin.class);
        // The site has light pages only, so the status bar keeps dark icons on the page background.
        AppCompatDelegate.setDefaultNightMode(AppCompatDelegate.MODE_NIGHT_NO);
        super.onCreate(savedInstanceState);
        if (isTv(this) && savedInstanceState == null && getBridge() != null && getBridge().getServerUrl() != null) {
            String url = Uri.parse(getBridge().getServerUrl()).buildUpon().encodedPath("/tv.html").clearQuery()
                .appendQueryParameter("name", tvName()).build().toString();
            getBridge().getWebView().loadUrl(url);
        }
    }

    /** Whether the app runs on a TV (Google TV, Android TV). */
    static boolean isTv(Context context) {
        UiModeManager uiMode = context.getSystemService(UiModeManager.class);
        return uiMode != null && uiMode.getCurrentModeType() == Configuration.UI_MODE_TYPE_TELEVISION;
    }

    /** The TV's name as its owner set it (e.g. "Living Room TV"), or its model. */
    private String tvName() {
        String name = Build.VERSION.SDK_INT >= Build.VERSION_CODES.N_MR1
            ? Settings.Global.getString(getContentResolver(), Settings.Global.DEVICE_NAME)
            : null;
        return name != null && !name.trim().isEmpty() ? name : Build.MODEL;
    }
}
