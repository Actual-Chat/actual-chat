using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Android.App;
using AndroidX.Core.App;
using Firebase.Analytics;
using Firebase.Messaging;
using DeviceType = ActualChat.Notifications.DeviceType;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui;

[Service(Exported = true)]
#pragma warning disable CA1861 // Prefer 'static readonly' fields over constant array arguments
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
#pragma warning restore CA1861
public class FirebaseMessagingService : Firebase.Messaging.FirebaseMessagingService
{
    private static ILogger? _log;
    /**
    * Request code used by display notification pending intents.
    *
    * Android only keeps one PendingIntent instance if it thinks multiple pending intents match.
    * Our intents often only differ by the payload which is stored in intent extras. As comparing
    * PendingIntents/Intents does not inspect the payload data, multiple pending intents, such as the
    * ones for click/dismiss will conflict.
    *
    * We also need to avoid conflicts with notifications started by an earlier launch of the app,
    * so use the truncated uptime of when the class was instantiated. The uptime will only overflow
    * every ~50 days, and even then chances of conflict will be rare.
    */

    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger<FirebaseMessagingService>();

 #pragma warning disable CS0169 // Field is never used
 #pragma warning disable CA1823
    // Keep reference to FirebaseAnalytics type to ensure FA package is used and will be initialized.
    private FirebaseAnalytics? _firebaseAnalytics;
 #pragma warning restore CA1823
 #pragma warning restore CS0169 // Field is never used

    public override void OnNewToken(string token)
    {
        // Same JNI boundary as OnMessageReceived: an escaping exception crashes the process.
        try {
            OnNewTokenImpl(token);
        }
        catch (Exception e) {
            Log.LogError(e, "OnNewToken failed");
        }
    }

    private void OnNewTokenImpl(string token)
    {
        Log.LogDebug("OnNewToken: '{Token}'", token);
        var appServices = IPlatformApplication.Current?.Services;
        var mauiNotifications = appServices?.GetService<MauiNotifications>();
        if (mauiNotifications is not null)
            _ = BackgroundTask.Run(
                () => mauiNotifications.RefreshNotificationToken(token, DeviceType.AndroidApp, CancellationToken.None),
                Log, "OnNewToken failed.");
        base.OnNewToken(token);
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        // This runs on an FCM dispatch thread and is called from Java: an escaping exception
        // crosses the JNI boundary and crashes the process, so nothing here may throw.
        try {
            OnMessageReceivedImpl(message);
        }
        catch (Exception e) {
            Log.LogError(e, "OnMessageReceived failed for message #{MessageId}", message.MessageId);
        }
    }

    private void OnMessageReceivedImpl(RemoteMessage message)
    {
        Log.LogDebug("OnMessageReceived: message #{MessageId}, CollapseKey='{CollapseKey}'" +
            ", Priority={Priority}, OriginalPriority={OriginalPriority}, IsDeprioritized={IsDeprioritized}",
            message.MessageId, message.CollapseKey, message.Priority, message.OriginalPriority, message.Priority != message.OriginalPriority);

        // There are 2 types of messages:
        // https://firebase.google.com/docs/cloud-messaging/concept-options#notifications_and_data_messages
        // Now we use Data message to deliver notifications to Android.
        // This allows us to control notification display style both when app is in foreground and in background modes.
        var dataRaw = message.Data.ToDictionary();
        if (Log.IsEnabled(LogLevel.Debug)) {
            var dataAsText = dataRaw.Select(c => $"'{c.Key}':'{c.Value}'").ToCommaPhrase();
            Log.LogDebug("OnMessageReceived: message #{MessageId}, Data: {Data}",
                message.MessageId, dataAsText.ToPrivate());
        }

        var data = new NotificationData(message.MessageId ?? "", dataRaw);

        if (data.DismissedTags.Count > 0) {
            var notificationManager = NotificationManagerCompat.From(this)!;
            foreach (var tag in data.DismissedTags)
                notificationManager.Cancel(tag, 0);
            if (data.DismissedTags.Any(t => t.StartsWith(Constants.Notification.CallTagPrefix, StringComparison.Ordinal)))
                IncomingCallActivity.FinishCurrent();
            ClearForegroundCallRings(data.DismissedTags);
            return;
        }

        if (data.NotificationKind == NotificationKind.IncomingCall) {
            HandleIncomingCall(data);
            return;
        }

        if (data.NotificationKind == NotificationKind.Attention
            && ShowGetAttentionNotification(data, message.SentTime))
            return;

        if (data.Title.IsNullOrEmpty() || data.Body.IsNullOrEmpty())
            return;

        var chatId = data.ChatId;
        if (chatId is not null && ShouldSuppressForDevice(chatId, data.EntryLocalId))
            return;

        ShowChatMessageNotification(data);
    }

    // Private methods

    // Decides whether this device should suppress the banner using its own (fresher-than-server)
    // read/view state. Fail-open: this runs on background deliveries too, where the Blazor scope
    // may be disposed and the scoped-service calls throw — a failed check must never suppress a
    // message or crash, so it returns false (show the notification) on any error.
    private static bool ShouldSuppressForDevice(ChatId chatId, long entryLid)
    {
        try {
            if (!TryGetScopedServices(out var scopedServices))
                return false;

            // Skip if the user is currently viewing this chat.
            if ((AndroidUtils.IsAppForeground() ?? false)
                && scopedServices.GetRequiredService<History>().LocalUrl.IsChat(out var currentChatId)
                && currentChatId == chatId) {
                Log.LogDebug("OnMessageReceived: notification in the current chat #{ChatId}", chatId);
                return true;
            }

            // Skip if this device has already read past the notification's entry. Uses the cached
            // read position (non-blocking) — it's fresher than the server's debounced cursor. If
            // there's no cached value (the chat was never opened here), don't suppress.
            if (entryLid > 0) {
                var chatUI = scopedServices.GetRequiredService<ChatUI>();
                // GetExisting can return an instance whose first computation is still in flight;
                // touching its output then throws "Wrong Computed.State: Computing."
                var cReadEntryLid = Computed.GetExisting(() => chatUI.GetReadEntryLid(chatId, default));
                if (cReadEntryLid is { ConsistencyState: not ConsistencyState.Computing }
                    && cReadEntryLid.IsValue(out var readEntryLid)
                    && readEntryLid >= entryLid) {
                    Log.LogDebug("OnMessageReceived: already read on this device #{ChatId} @ {EntryLid}", chatId, entryLid);
                    return true;
                }
            }

            return false;
        }
        catch (Exception e) {
            Log.LogWarning(e, "ShouldSuppressForDevice failed for chat #{ChatId}; showing the notification", chatId);
            return false;
        }
    }
    private static void ClearForegroundCallRings(IReadOnlyList<string> dismissedTags)
    {
        // A foreground ring lives in the in-app banner/ringer, not a system notification, so a
        // cancel/decline/timeout dismissal must reach IncomingCallUI directly — the reactive
        // live-session computed (NoCache) would otherwise clear the ring only on its slow self-heal.
        if (!(AndroidUtils.IsAppForeground() ?? false) || !TryGetScopedServices(out _))
            return;

        foreach (var tag in dismissedTags) {
            if (!tag.StartsWith(Constants.Notification.CallTagPrefix))
                continue;

            var chatId = ChatId.TryParse(tag[Constants.Notification.CallTagPrefix.Length..], allowNull: true);
            if (chatId is null)
                continue;

            _ = DispatchToBlazor(
                c => c.GetRequiredService<IncomingCallUI>().OnCallDismissed(chatId),
                "IncomingCallUI.OnCallDismissed");
        }
    }

    private static void HandleIncomingCall(NotificationData data)
    {
        var chatId = data.ChatId;
        if (chatId is null) {
            Log.LogWarning("Can't handle incoming-call push. Invalid ChatId. Ref messageId: '{MessageId}'", data.MessageId);
            return;
        }

        // The system notification (silent channel) is always shown; its full-screen intent opens
        // the native IncomingCallActivity over the lock screen / in the background. Whenever the
        // Blazor scope is alive we also register the ring so the in-app banner + ringer run. (The
        // shared IncomingCallRinger is idempotent, so an overlap with the native screen can't double.)
        IncomingCallNotifications.Show(data);
        if (TryGetScopedServices(out _))
            _ = DispatchToBlazor(
                c => c.GetRequiredService<IncomingCallUI>().OnRing(chatId),
                "IncomingCallUI.OnRing");
    }

    private static bool ShowGetAttentionNotification(
        NotificationData data,
        long messageSentTime)
    {
        var chatId = data.ChatId;
        if (chatId is null) {
            Log.LogWarning("Can't show get-attention notification. Invalid ChatId. Ref messageId: '{MessageId}'", data.MessageId);
            return false;
        }

        var sentTime = new Moment(messageSentTime * 10_000).ToDateTime();
        var title = data.Title ?? "";
        var separatorIndex = title.IndexOf('@');
        if (separatorIndex >= 0)
            title = title.Substring(separatorIndex + 1).Trim(); // take Chat title only

        var request = new ChatAttentionRequest(chatId, data.LastEntryLocalId, sentTime, title, data.Body ?? "", data.ImageUrl ?? "");
        ChatAttentionService.Instance.Ask(request);
        return true;
    }

    private void ShowChatMessageNotification(NotificationData data)
    {
        Log.LogDebug("-> ShowChatMessageNotification, text: '{Text}', silent: {Silent}", data.Body!.ToPrivate(), data.Silent);
        NotificationHelper.ShowChatNotification(
            data.Tag!, data.Title!, data.Body!, data.ImageUrl, data.Link, data.Silent, data.Messages);
    }
}
