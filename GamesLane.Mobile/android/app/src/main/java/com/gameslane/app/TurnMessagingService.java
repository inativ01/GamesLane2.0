package com.gameslane.app;

import androidx.annotation.NonNull;
import com.google.firebase.messaging.FirebaseMessagingService;
import com.google.firebase.messaging.RemoteMessage;

/**
 * Turn notifications from the server (Firebase Cloud Messaging). While the app is not running, Android shows them
 * itself, and a tap opens the app with the notification's data as extras ({@link GamesLanePlugin#EXTRA_PATH}); while it
 * is running, they come here and are shown the same way (the server sends one only when the game's page is not in view).
 */
public class TurnMessagingService extends FirebaseMessagingService {

    @Override
    public void onMessageReceived(@NonNull RemoteMessage message) {
        RemoteMessage.Notification notification = message.getNotification();
        if (notification == null) {
            return;
        }
        GamesLanePlugin.showTurnNotification(
            this,
            notification.getTitle(),
            notification.getBody(),
            message.getData().get(GamesLanePlugin.EXTRA_PATH),
            message.getData().get(GamesLanePlugin.EXTRA_SESSION)
        );
    }

    @Override
    public void onNewToken(@NonNull String token) {
        // The site registers the device again on every page it opens, with the new token.
    }
}
