using ActualChat.App.Maui.Audio;
using ActualChat.App.Maui.Services;
using ActualLab.Diagnostics;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Firebase.Analytics;
using Firebase.Messaging;
using DeviceType = ActualChat.Notifications.DeviceType;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using NotificationDismissMode = ActualChat.Notifications.NotificationDismissMode;
using NotificationExt = ActualChat.Notifications.NotificationExt;

namespace ActualChat.App.Maui;

[Service(Exported = true)]
#pragma warning disable CA1861 // Prefer 'static readonly' fields over constant array arguments
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
#pragma warning restore CA1861
public sealed class FirebaseMessagingService : Firebase.Messaging.FirebaseMessagingService
{
    private static readonly TimeSpan FirebaseReadyTimeout = TimeSpan.FromSeconds(15);
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger<FirebaseMessagingService>();
    private static ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.AndroidIncomingCalls);

#pragma warning disable CS0169 // Field is never used
#pragma warning disable CA1823
    // Keep reference to FirebaseAnalytics type to ensure FA package is used and will be initialized.
    private FirebaseAnalytics? _firebaseAnalytics;
#pragma warning restore CA1823
#pragma warning restore CS0169 // Field is never used

    public override void HandleIntent(Intent? intent)
    {
        // FCM's own path calls MessagingAnalytics.logNotificationReceived - FirebaseApp.getInstance
        // included - for messages carrying an analytics label, which the server sets on dev and for
        // opted-in users. This process may have been started by this very broadcast, with
        // MauiFirebase still initializing on its thread; this runs on FCM's executor, so waiting is
        // fine. On a timeout the message goes through anyway: FCM's exception is the lesser evil
        // next to a swallowed notification.
        try {
            if (!MauiFirebase.WhenReady.Wait(FirebaseReadyTimeout))
                Log.LogWarning("HandleIntent: Firebase isn't ready after {Timeout}", FirebaseReadyTimeout);
        }
        catch (Exception e) {
            Log.LogWarning(e, "HandleIntent: Firebase init failed");
        }
        base.HandleIntent(intent);
    }

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
            message.MessageId, message.CollapseKey, message.Priority, message.OriginalPriority,
            message.Priority != message.OriginalPriority);

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
            // Read before cancelling: afterwards nothing tells which call notification was on screen.
            ChatId[] shownCallChatIds;
            try {
                shownCallChatIds = IncomingCallNotifications.ListActiveCallChatIds();
            }
            catch (Exception e) {
                Log.LogWarning(e, "Couldn't list the shown call notifications; dismissing without stopping the ring");
                shownCallChatIds = [];
            }
            var notificationManager = NotificationManagerCompat.From(this)!;
            foreach (var tag in data.DismissedTags)
                notificationManager.Cancel(tag, 0);
            ClearAttentionRequests(data.DismissedTags);
            ClearForegroundCallRings(data.DismissedTags);
            StopRingForDismissedCalls(data.DismissedTags, shownCallChatIds);

            return;
        }

        if (data.NotificationKind == NotificationKind.IncomingCall) {
            HandleIncomingCall(data);
            return;
        }

        if (data.NotificationKind == NotificationKind.SpeechStarted) {
            PttWakeHandler.Handle(data);
            return;
        }

        if (data.NotificationKind == NotificationKind.Attention
            && ShowGetAttentionNotification(data, message.SentTime))
            return;

        if (data.Title.IsNullOrEmpty() || data.Body.IsNullOrEmpty())
            return;

        var chatId = data.ChatId;
        if (chatId is not null && ShouldSuppressForDevice(chatId, data.EntryLocalId, data.NotificationKind))
            return;

        ShowChatMessageNotification(data);
    }

    // Private methods

    private static bool ShouldSuppressForDevice(ChatId chatId, long entryLid, NotificationKind kind)
    {
        // Fail-open: this runs on background deliveries too, where the Blazor scope may be disposed
        // and the scoped-service calls throw - a failed check must never suppress a message or
        // crash, so it returns false (show the notification) on any error.
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
            // Only for kinds a read actually clears: a reaction anchors at the recipient's own
            // message, so its entry is read the moment it was sent and this would drop every one.
            if (entryLid > 0 && NotificationExt.GetDismissMode(kind) == NotificationDismissMode.OnRead) {
                var chatUI = scopedServices.GetRequiredService<ChatUI>();
                // GetExisting can return an instance whose first computation is still in flight;
                // touching its output then throws "Wrong Computed.State: Computing."
                var cReadEntryLid = Computed.GetExisting(() => chatUI.GetReadEntryLid(chatId, default));
                if (cReadEntryLid is { ConsistencyState: not ConsistencyState.Computing }
                    && cReadEntryLid.IsValue(out var readEntryLid)
                    && readEntryLid >= entryLid) {
                    Log.LogDebug("OnMessageReceived: already read on this device #{ChatId} @ {EntryLid}",
                        chatId, entryLid);
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

    private static void ClearAttentionRequests(IReadOnlyList<string> dismissedTags)
    {
        // An attention banner lives under ChatAttentionService's own tag, so cancelling the dismissed
        // one never touches it. Attention tags by entry, so the tag is usually an entry id.
        var chatIds = dismissedTags
            .Select(tag => ChatEntryId.TryParse(tag, out var entryId)
                ? entryId.ChatId
                : ChatId.TryParse(tag, allowNull: true))
            .SkipNullItems()
            .Distinct()
            .ToList();
        if (chatIds.Count > 0)
            ChatAttentionService.Instance.Dismiss(chatIds);
    }

    private static void ClearForegroundCallRings(IReadOnlyList<string> dismissedTags)
    {
        // A foreground ring lives in the in-app call UI/ringer, not a system notification, so a
        // cancel/decline/timeout dismissal must reach CallScreensUI directly — the reactive
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
                c => c.GetRequiredService<CallScreensUI>().OnCallDismissed(chatId),
                "CallScreensUI.OnCallDismissed");
        }
    }

    private static void StopRingForDismissedCalls(IReadOnlyList<string> dismissedTags, ChatId[] shownCallChatIds)
    {
        // The ringtone that started with a shown call notification goes with it. A ring Blazor drives is stopped
        // by CallScreensUI too; a double stop is harmless.
        var isShownCallDismissed = dismissedTags
            .Select(IncomingCallNotifications.TryParseCallTag)
            .Any(chatId => chatId is not null && shownCallChatIds.Contains(chatId));
        if (isShownCallDismissed)
            IncomingCallRinger.Stop();
    }

    private static void HandleIncomingCall(NotificationData data)
    {
        var chatId = data.ChatId;
        if (chatId is null) {
            Log.LogWarning("Can't handle incoming-call push. Invalid ChatId. Ref messageId: '{MessageId}'",
                data.MessageId);
            return;
        }

        var scopeAlive = TryGetScopedServices(out _);
        var isForeground = AndroidUtils.IsAppForeground();
        DebugLog?.LogInformation(
            "CALL_TRACE: HandleIncomingCall push #{ChatId}, scopeAlive={ScopeAlive}, foreground={Foreground}",
            chatId, scopeAlive, isForeground);
        // Foreground + unlocked: the in-app call UI and ringer own the ring, so the system CallStyle
        // notification would only stack a heads-up banner over them. Show it just when the app is
        // backgrounded, killed, or locked - where its full-screen intent is the only way to reach the user.
        if (scopeAlive && isForeground == true) {
            _ = DispatchToBlazor(
                c => c.GetRequiredService<CallScreensUI>().OnRing(chatId),
                "CallScreensUI.OnRing");
            return;
        }

        // No arbitration here any more: the server owns the user's call, so a ring that shouldn't be
        // shown is never pushed in the first place - see CallsBackend.
        // The channel is silent, so the ringtone starts with the notification, or it's a missed call - but
        // not while notifications are blocked: nothing was posted to answer or silence it.
        IncomingCallNotifications.Show(data);
        if (IncomingCallNotifications.CanPostCalls())
            IncomingCallRinger.Start(Constants.Call.RingTimeout);
        if (scopeAlive)
            _ = DispatchToBlazor(
                c => c.GetRequiredService<CallScreensUI>().OnRing(chatId),
                "CallScreensUI.OnRing");
    }

    private static bool ShowGetAttentionNotification(NotificationData data, long messageSentTime)
    {
        var chatId = data.ChatId;
        if (chatId is null) {
            Log.LogWarning("Can't show get-attention notification. Invalid ChatId. Ref messageId: '{MessageId}'",
                data.MessageId);
            return false;
        }

        var sentTime = new Moment(messageSentTime * 10_000).ToDateTime();
        // Names the chat, which for a peer chat is the other party - it carries no group title.
        var title = data.GroupTitle ?? data.SenderName ?? "";

        var request = new ChatAttentionRequest(
            chatId, data.LastEntryLocalId, sentTime, title, data.Body ?? "", data.ImageUrl ?? "");
        ChatAttentionService.Instance.Ask(request);
        return true;
    }

    private void ShowChatMessageNotification(NotificationData data)
    {
        Log.LogDebug("-> ShowChatMessageNotification, text: '{Text}', silent: {Silent}",
            data.Body!.ToPrivate(), data.Silent);
        NotificationHelper.ShowChatNotification(
            data.ChatId, data.Tag!, data.Title!, data.Body!, data.ImageUrl, data.Link,
            data.Silent, data.Messages, data.SenderName, data.GroupTitle);
    }
}
