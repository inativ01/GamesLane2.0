package com.gameslane.app;

import android.os.Bundle;
import androidx.appcompat.app.AppCompatDelegate;
import com.getcapacitor.BridgeActivity;

/** The app's screen: Capacitor's web view, with the GamesLane plugin. */
public class MainActivity extends BridgeActivity {

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        registerPlugin(GamesLanePlugin.class);
        // The site has light pages only, so the status bar keeps dark icons on the page background.
        AppCompatDelegate.setDefaultNightMode(AppCompatDelegate.MODE_NIGHT_NO);
        super.onCreate(savedInstanceState);
    }
}
