package com.gameslane.app;

import android.Manifest;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.os.VibratorManager;
import android.view.WindowManager;
import android.webkit.WebView;
import androidx.activity.OnBackPressedCallback;
import androidx.activity.result.ActivityResultLauncher;
import androidx.browser.auth.AuthTabIntent;
import androidx.core.app.NotificationCompat;
import androidx.core.app.NotificationManagerCompat;
import androidx.core.content.ContextCompat;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;
import com.getcapacitor.annotation.Permission;
import com.getcapacitor.annotation.PermissionCallback;
import com.google.firebase.messaging.FirebaseMessaging;

/**
 * The app's own features for the site, which calls them as {@code window.Capacitor.Plugins.GamesLane} (see common.js),
 * the same as the iPhone app's (ios/App/App/GamesLanePlugin.swift): signing in with a provider in a Chrome Auth Tab,
 * vibrating when the user's turn comes, turn notifications, and keeping the screen on during a game. Join links (App
 * Links, https://gameslane.onrender.com/join/...) and tapped notifications open their page in the app's web view, and
 * the back button goes back in it.
 */
@CapacitorPlugin(
    name = "GamesLane",
    permissions = @Permission(strings = { Manifest.permission.POST_NOTIFICATIONS }, alias = GamesLanePlugin.NOTIFICATIONS)
)
public class GamesLanePlugin extends Plugin {

    /** The page a turn notification opens (its {@code path}, e.g. /backgammon.html?session=ABC123). */
    static final String EXTRA_PATH = "path";

    /** The session a turn notification is about, so a newer one about the same session replaces it. */
    static final String EXTRA_SESSION = "session";

    static final String NOTIFICATIONS = "notifications";

    /** The address on the site that ends a sign-in (the server's AuthSetup.AppReturnPath). */
    private static final String APP_RETURN_PATH = "/auth/app-return";

    /** How long a closed Custom Tab waits for the sign-in's App Link, which may come just after it closes. */
    private static final long CUSTOM_TAB_RETURN_WAIT_MS = 1500;

    private ActivityResultLauncher<Intent> authTab;
    private PluginCall signInCall;
    private String signInCallbackPath;

    @Override
    public void load() {
        authTab = AuthTabIntent.registerActivityResultLauncher(getActivity(), this::onAuthTabResult);
        createTurnChannel(getContext());
        getActivity()
            .getOnBackPressedDispatcher()
            .addCallback(
                getActivity(),
                new OnBackPressedCallback(true) {
                    @Override
                    public void handleOnBackPressed() {
                        WebView webView = getBridge().getWebView();
                        if (webView.canGoBack()) {
                            webView.goBack();
                        } else {
                            getActivity().moveTaskToBack(true);
                        }
                    }
                }
            );
    }

    /**
     * Opens {@code url} (the server's /auth/signin/{provider}?app=true...) in a Chrome Auth Tab, and resolves with
     * {@code { url }}, the address the server ended the sign-in with: {@code callback}, an https address on the site
     * (verified by the site's assetlinks.json). A browser without Auth Tab opens a Custom Tab instead, and the callback
     * comes back as an App Link (see {@link #handleOnNewIntent}). Rejects with the code CANCELLED when the user closes
     * the tab.
     */
    @PluginMethod
    public void signIn(PluginCall call) {
        Uri url = Uri.parse(call.getString("url", ""));
        Uri callback = Uri.parse(call.getString("callback", ""));
        if (!"https".equals(url.getScheme()) || !"https".equals(callback.getScheme()) || callback.getHost() == null) {
            call.reject("An https sign-in address and callback are required.");
            return;
        }

        getActivity()
            .runOnUiThread(() -> {
                if (signInCall != null) {
                    signInCall.reject("Sign-in was cancelled.", "CANCELLED");
                }
                signInCall = call;
                signInCallbackPath = callback.getPath();
                try {
                    new AuthTabIntent.Builder().build().launch(authTab, url, callback.getHost(), callback.getPath());
                } catch (ActivityNotFoundException e) {
                    signInCall = null;
                    call.reject("Sign-in could not start.");
                }
            });
    }

    private void onAuthTabResult(AuthTabIntent.AuthResult result) {
        if (signInCall == null) {
            return;
        }
        if (result.resultCode == AuthTabIntent.RESULT_OK && result.resultUri != null) {
            resolveSignIn(result.resultUri);
        } else if (result.resultCode == AuthTabIntent.RESULT_CANCELED) {
            PluginCall call = signInCall;
            new Handler(Looper.getMainLooper()).postDelayed(
                () -> {
                    if (signInCall == call) {
                        signInCall = null;
                        call.reject("Sign-in was cancelled.", "CANCELLED");
                    }
                },
                CUSTOM_TAB_RETURN_WAIT_MS
            );
        } else {
            PluginCall call = signInCall;
            signInCall = null;
            call.reject("Sign-in did not complete.");
        }
    }

    private void resolveSignIn(Uri url) {
        PluginCall call = signInCall;
        signInCall = null;
        JSObject result = new JSObject();
        result.put("url", url.toString());
        call.resolve(result);
    }

    /**
     * A join link or a sign-in's end (App Links), or a tapped turn notification ({@link #EXTRA_PATH}), that opened the
     * app or came while it was open. Capacitor also passes the intent the app was launched with, once its first page
     * starts loading, so the link's page replaces it.
     */
    @Override
    protected void handleOnNewIntent(Intent intent) {
        Uri link = intent.getData();
        if (link != null && "https".equals(link.getScheme()) && getContext().getString(R.string.app_link_host).equals(link.getHost())) {
            intent.setData(null);
            if (APP_RETURN_PATH.equals(link.getPath())) {
                if (signInCall != null && APP_RETURN_PATH.equals(signInCallbackPath)) {
                    resolveSignIn(link);
                }
            } else {
                open(link.getEncodedPath(), link.getEncodedQuery());
            }
        }

        String path = intent.getStringExtra(EXTRA_PATH);
        if (path != null) {
            intent.removeExtra(EXTRA_PATH);
            Uri page = Uri.parse(path);
            if (page.getHost() == null && page.getEncodedPath() != null && page.getEncodedPath().startsWith("/")) {
                open(page.getEncodedPath(), page.getEncodedQuery());
            }
        }
    }

    /** Opens {@code path} (with {@code query}) in the web view, on the server the app shows. */
    private void open(String path, String query) {
        String server = getBridge().getServerUrl();
        if (server == null || path == null) {
            return;
        }
        Uri url = Uri.parse(server).buildUpon().encodedPath(path).encodedQuery(query).fragment(null).build();
        WebView webView = getBridge().getWebView();
        webView.post(() -> webView.loadUrl(url.toString()));
    }

    /**
     * Registers the device for notifications and resolves with {@code { token, sandbox, platform }}: Firebase's
     * registration token for the app, {@code false}, and {@code "android"}. With {@code ask}, the user is asked to allow
     * notifications (Android 13 and later) if they are not allowed yet. Rejects with the code DENIED when notifications
     * are not allowed.
     */
    @PluginMethod
    public void registerForNotifications(PluginCall call) {
        if (notificationsAllowed(getContext())) {
            requestToken(call);
        } else if (call.getBoolean("ask", false) && Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            requestPermissionForAlias(NOTIFICATIONS, call, "notificationPermission");
        } else {
            call.reject("Notifications are not allowed.", "DENIED");
        }
    }

    @PermissionCallback
    private void notificationPermission(PluginCall call) {
        if (notificationsAllowed(getContext())) {
            requestToken(call);
        } else {
            call.reject("Notifications are not allowed.", "DENIED");
        }
    }

    private void requestToken(PluginCall call) {
        FirebaseMessaging messaging;
        try {
            messaging = FirebaseMessaging.getInstance();
        } catch (IllegalStateException e) {
            // Built without google-services.json.
            call.reject("Notifications are not set up in this build.");
            return;
        }
        messaging
            .getToken()
            .addOnCompleteListener(task -> {
                if (task.isSuccessful() && task.getResult() != null) {
                    JSObject result = new JSObject();
                    result.put("token", task.getResult());
                    result.put("sandbox", false);
                    result.put("platform", "android");
                    call.resolve(result);
                } else {
                    call.reject("Could not register for notifications.", null, task.getException());
                }
            });
    }

    static boolean notificationsAllowed(Context context) {
        return NotificationManagerCompat.from(context).areNotificationsEnabled();
    }

    /** The channel of turn notifications: they make a sound and show on the screen, as on the iPhone. */
    static void createTurnChannel(Context context) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
            return;
        }
        NotificationChannel channel = new NotificationChannel(
            context.getString(R.string.turn_channel_id),
            context.getString(R.string.turn_channel_name),
            NotificationManager.IMPORTANCE_HIGH
        );
        context.getSystemService(NotificationManager.class).createNotificationChannel(channel);
    }

    /**
     * Shows a turn notification that came while the app was running (see {@link TurnMessagingService}); tapping it
     * opens {@code path}. A newer one about the same {@code session} replaces it.
     */
    static void showTurnNotification(Context context, String title, String body, String path, String session) {
        if (!notificationsAllowed(context)) {
            return;
        }
        createTurnChannel(context);
        Intent open = new Intent(context, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        if (path != null) {
            open.putExtra(EXTRA_PATH, path);
        }
        String tag = session != null ? session : "";
        PendingIntent tap = PendingIntent.getActivity(
            context,
            tag.hashCode(),
            open,
            PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE
        );
        NotificationCompat.Builder notification = new NotificationCompat.Builder(context, context.getString(R.string.turn_channel_id))
            .setSmallIcon(R.drawable.ic_stat_turn)
            .setColor(ContextCompat.getColor(context, R.color.notification_color))
            .setContentTitle(title)
            .setContentText(body)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setAutoCancel(true)
            .setContentIntent(tap);
        try {
            NotificationManagerCompat.from(context).notify(tag, 0, notification.build());
        } catch (SecurityException e) {
            // Notifications were turned off meanwhile.
        }
    }

    /** Vibrates the phone once, as the site's turn vibration in browsers. */
    @PluginMethod
    public void vibrate(PluginCall call) {
        long[] pattern = { 0, 180, 90, 180 };
        Vibrator vibrator = Build.VERSION.SDK_INT >= Build.VERSION_CODES.S
            ? getContext().getSystemService(VibratorManager.class).getDefaultVibrator()
            : (Vibrator) getContext().getSystemService(Context.VIBRATOR_SERVICE);
        if (vibrator != null && vibrator.hasVibrator()) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                vibrator.vibrate(VibrationEffect.createWaveform(pattern, -1));
            } else {
                vibrator.vibrate(pattern, -1);
            }
        }
        call.resolve();
    }

    /** Keeps the screen on ({@code on: true}) or lets it dim and lock as usual ({@code on: false}). */
    @PluginMethod
    public void keepAwake(PluginCall call) {
        boolean on = call.getBoolean("on", false);
        getActivity()
            .runOnUiThread(() -> {
                if (on) {
                    getActivity().getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
                } else {
                    getActivity().getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
                }
                call.resolve();
            });
    }
}
