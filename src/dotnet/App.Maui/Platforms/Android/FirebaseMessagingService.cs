using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Notification;
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Firebase.Messaging;
using NetBox.Extensions;

namespace ActualChat.App.Maui;

[Service(Exported = true)]
[IntentFilter(new[] { "com.google.firebase.MESSAGING_EVENT" })]
public class FirebaseMessagingService : Firebase.Messaging.FirebaseMessagingService
{
    //public override void OnNewToken(string token)
    //{
    //    base.OnNewToken(token);
    //    if (Preferences.ContainsKey("DeviceToken"))
    //        Preferences.Remove("DeviceToken");
    //    Preferences.Set("DeviceToken", token);
    //}

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);

        // There are 2 types of messages:
        // https://firebase.google.com/docs/cloud-messaging/concept-options#notifications_and_data_messages
        var notification = message.GetNotification();
        if (notification == null) {
            // Data message is delivered both in foreground and background modes.
            // For now we have no messages of this type.
            // Nothing to do.
            return;
        }

        // Notification message is delivered here only when app is foreground.
        var data = message.Data;
        data.TryGetValue(NotificationConstants.MessageDataKeys.ChatId, out var chatId);
        if (!chatId.IsNullOrEmpty() && ScopedServiceLocator.IsInitialized) {
            var chatUI = ScopedServiceLocator.Services.GetRequiredService<ChatUI>();
            if (!chatId.IsNullOrEmpty() && OrdinalEquals(chatUI.ActiveChatId.Value, chatId))
                return; // Do nothing if notification came for the active chat.
        }

        SendNotification(notification.Body, notification.Title, message.Data);
    }

    private void SendNotification(string messageBody, string title, IDictionary<string, string> data)
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.SingleTop);

        foreach (var key in data.Keys) {
            string value = data[key];
            intent.PutExtra(key, value);
        }

        var pendingIntent = PendingIntent.GetActivity(this,
            MainActivity.NotificationID, intent, PendingIntentFlags.OneShot | PendingIntentFlags.Immutable);

        var notificationBuilder = new NotificationCompat.Builder(this, NotificationConstants.ChannelIds.Default)
            .SetContentTitle(title)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentText(messageBody)
            .SetContentIntent(pendingIntent)
            .SetAutoCancel(true) // closes notification after tap
            .SetPriority((int)NotificationPriority.High);

        var notificationManager = NotificationManagerCompat.From(this);
        notificationManager.Notify(MainActivity.NotificationID, notificationBuilder.Build());
    }
}
