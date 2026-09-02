using System.Text;
using ActualLab.Diagnostics;
using FirebaseAdmin.Messaging;

namespace ActualChat.Notifications;

public class FirebaseMessagingClient(
    UrlMapper urlMapper,
    FirebaseMessaging firebaseMessaging,
    ICommander commander,
    ILogger<FirebaseMessagingClient> log)
    : IFirebaseMessagingClient
{
    private const int MaxFcmPayloadBytes = 4096;
    private const int FcmPayloadMargin = 512;

    private UrlMapper UrlMapper { get; } = urlMapper;
    private FirebaseMessaging FirebaseMessaging { get; } = firebaseMessaging;
    private ICommander Commander { get; } = commander;
    private ILogger Log { get; } = log;
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Notifications);

    public async Task SendMessage(
        Notification notification,
        IReadOnlyCollection<Symbol> deviceIds,
        bool? enableDataCollection,
        int badgeCount,
        bool isSilent,
        CancellationToken cancellationToken)
    {
        var notificationId = notification.Id;
        var kind = notification.Kind;
        var title = notification.Title;
        var content = notification.Text;
        var iconUrl = notification.IconUrl;
        var chatNotification = notification as ChatNotification;
        var chatId = chatNotification?.ChatId;
        // entryId carries the latest entry (client read-skip), linkEntryId the first-unread tap target.
        var entryId = (ChatEntryId?)null;
        var linkEntryId = (ChatEntryId?)null;
        long lastEntryLocalId = 0;
        switch (notification) {
        case AttentionNotification attention:
            lastEntryLocalId = attention.EntryLid;
            break;
        case ChatEntryRelatedNotification related when related.EntryLid != 0:
            entryId = related.EntryId;
            linkEntryId = related.StartEntryId;
            break;
        case ChatEntryNotification entry:
            entryId = entry.EntryId;
            linkEntryId = entry.EntryId;
            break;
        case ConversationNotification conversation:
            entryId = ChatEntryId.New(conversation.ChatId, conversation.StartEntryLid);
            linkEntryId = entryId;
            break;
        }

        var absoluteIconUrl = UrlMapper.ToAbsolute(iconUrl, true);
        var isDev = UrlMapper.IsDevVoxt;
        // Attention pings and incoming calls ring with elevated priority + a ringtone.
        var isRinger = kind is NotificationKind.Attention or NotificationKind.IncomingCall;

        var isChatRelated = chatId is not null;
        var isEntryRelated = linkEntryId is not null;
        var tag = notification.GetPushTag() ?? "topic";
        // iOS stacks same-thread banners under one group; mentions keep their own banner (tag)
        // but still stack with the rest of their chat.
        var threadTag = notification.GetChatTag() ?? tag;
        var link = isEntryRelated ? UrlMapper.ToAbsolute(Links.Chat(linkEntryId))
            : isChatRelated ? UrlMapper.ToAbsolute(Links.Chat(chatId!))
            : "";

        // Common data: every platform gets these, and on APNs they ride alongside aps in the same
        // 4KB budget - so the renderer-only keys below stay out of it. iOS renders from aps.alert
        // and reads only Link from data, so it needs none of them.
        var data = new Dictionary<string, string>() {
            { Constants.Notification.MessageDataKeys.NotificationId, notificationId.Value },
            { Constants.Notification.MessageDataKeys.Tag, tag },
            { Constants.Notification.MessageDataKeys.ChatId, chatId?.Value ?? "" },
            { Constants.Notification.MessageDataKeys.ChatEntryId, entryId?.Value ?? "" },
            { Constants.Notification.MessageDataKeys.Icon, absoluteIconUrl },
            { Constants.Notification.MessageDataKeys.Kind, kind.ToString() },
            { Constants.Notification.MessageDataKeys.Link, link },
            { Constants.Notification.MessageDataKeys.Silent, isSilent.ToString() },
            {
                Constants.Notification.MessageDataKeys.Timestamp,
                ((long)notification.CreatedAt.EpochOffset.TotalMilliseconds).ToString()
            },
        };
        if (lastEntryLocalId > 0)
            data.Add(Constants.Notification.MessageDataKeys.LastEntryLocalId, lastEntryLocalId.ToString());
        // Common data, so these ride inside the 4KB APNs budget - hence omitted when empty.
        if (chatNotification is not null && !chatNotification.SenderName.IsNullOrEmpty()) {
            data.Add(Constants.Notification.MessageDataKeys.SenderName, chatNotification.SenderName);
            if (!chatNotification.GroupTitle.IsNullOrEmpty())
                data.Add(Constants.Notification.MessageDataKeys.GroupTitle, chatNotification.GroupTitle);
        }
        // Android and web build the banner themselves, so they also need its content. Both platform
        // blocks override the common data rather than merging into it, hence the copy.
        var renderData = new Dictionary<string, string>(data) {
            { Constants.Notification.MessageDataKeys.Title, title },
            { Constants.Notification.MessageDataKeys.Body, content },
            { Constants.Notification.MessageDataKeys.ImageUrl, absoluteIconUrl },
        };
        if (notification is ChatEntryRelatedNotification { RecentMessages.Count: > 0 } coalesced) {
            // FCM rejects messages over 4KB, which would drop the push entirely — so the transcript
            // key gets only the space the other data values leave, and is omitted when even one
            // message can't fit (the Body fallback still renders).
            var usedBytes = renderData
                .Sum(kv => Encoding.UTF8.GetByteCount(kv.Key) + Encoding.UTF8.GetByteCount(kv.Value));
            var budget = Math.Min(PushMessage.MaxJsonLength, MaxFcmPayloadBytes - FcmPayloadMargin - usedBytes);
            var json = PushMessage.ToJson(coalesced.RecentMessages, budget);
            if (!json.IsNullOrEmpty())
                renderData.Add(Constants.Notification.MessageDataKeys.Messages, json);
        }
        var multicastMessage = new MulticastMessage {
            Tokens = deviceIds.Select(id => id.Value).ToList(),
            // Data-only on every platform except APNs: each client renders the banner itself.
            // A Webpush.Notification here would make the FCM SDK render its own banner *in addition
            // to* the one our service worker shows - two banners, two alert sounds, and the SDK's
            // copy ignores the Silent key, so even a silent content update would beep.
            Data = data,
            Android = new AndroidConfig {
                Data = renderData,
                Priority = Priority.High,
                // CollapseKey = default, /* We don't use collapsible messages */
                TimeToLive = TimeSpan.FromDays(10),
            },
            Apns = new ApnsConfig {
                Headers = new Dictionary<string, string>() {
                    ["apns-push-type"] = "alert",
                    ["apns-priority"] = isRinger ? "10" : "5",
                    // Coalesce updates for the same banner instead of stacking.
                    ["apns-collapse-id"] = tag,
                },
                Aps = new Aps {
                    Alert = new ApsAlert {
                        Title = title,
                        Body = content,
                    },
                    // iOS only updates a backgrounded app's icon badge from aps.badge -> always send it.
                    Badge = badgeCount,
                    // A silent update refreshes the banner content without playing a sound.
                    Sound = isSilent ? null : isRinger ? "attention_ringtone.caf" : "default",
                    MutableContent = true,
                    ThreadId = threadTag,
                },
                FcmOptions = new ApnsFcmOptions {
                    ImageUrl = absoluteIconUrl,
                },
            },
            Webpush = new WebpushConfig {
                Data = renderData,
            },
        };
        if (isDev || enableDataCollection.GetValueOrDefault())
            multicastMessage.Android.FcmOptions = new AndroidFcmOptions {
                AnalyticsLabel = "dev_test" // Add label to see data messages statistics in Message delivery reports.
            };
        var batchResponse = await FirebaseMessaging
            .SendEachForMulticastAsync(multicastMessage, cancellationToken)
            .ConfigureAwait(false);
        await HandleBatchResponse(batchResponse, deviceIds, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<PendingDismissal>> SendDismissal(
        IReadOnlyCollection<PendingDismissal> dismissals,
        IReadOnlyCollection<Symbol> deviceIds,
        int badgeCount,
        CancellationToken cancellationToken)
    {
        if (deviceIds.Count == 0)
            return [];

        var sent = new List<PendingDismissal>();
        foreach (var chunk in ChunkDismissals(dismissals)) {
            var data = new Dictionary<string, string>() {
                {
                    Constants.Notification.MessageDataKeys.DismissedIds,
                    string.Join(',', chunk.Dismissals.Select(x => x.Id.Value))
                },
                { Constants.Notification.MessageDataKeys.DismissedTags, string.Join(',', chunk.Tags) },
            };
            var multicastMessage = new MulticastMessage {
                Tokens = deviceIds.Select(id => id.Value).ToList(),
                Data = data,
                Android = new AndroidConfig {
                    Data = data,
                    // Doze holds this one, but only until the device wakes: measured at ~0.57s
                    // after it leaves Doze, which High would buy with an FCM cold start.
                    Priority = Priority.Normal,
                    TimeToLive = TimeSpan.FromDays(1),
                },
                Apns = new ApnsConfig {
                    Headers = new Dictionary<string, string>() {
                        // A silent push: it only updates the badge and lets the app drop dismissed notifications.
                        ["apns-push-type"] = "background",
                        ["apns-priority"] = "5",
                    },
                    Aps = new Aps {
                        // No Badge here: a background notification's aps may carry only
                        // content-available, and iOS ignores a badge sent alongside it - measured,
                        // banners cleared while the count stayed put. SendBadge carries it instead.
                        ContentAvailable = true,
                    },
                },
            };
            var batchResponse = await FirebaseMessaging
                .SendEachForMulticastAsync(multicastMessage, cancellationToken)
                .ConfigureAwait(false);
            await HandleBatchResponse(batchResponse, deviceIds, cancellationToken).ConfigureAwait(false);
            if (IsAccepted(batchResponse))
                sent.AddRange(chunk.Dismissals);
        }

        return sent;
    }

    public async Task SendBadge(
        IReadOnlyCollection<Symbol> deviceIds,
        int badgeCount,
        CancellationToken cancellationToken)
    {
        if (deviceIds.Count == 0)
            return;

        var multicastMessage = new MulticastMessage {
            Tokens = deviceIds.Select(id => id.Value).ToList(),
            Apns = new ApnsConfig {
                Headers = new Dictionary<string, string>() {
                    // "alert" is the push type for anything that triggers an alert, badge or sound,
                    // and badge-only is one of those. It matters: "background" is the throttled
                    // budget this exists to get out of. With no alert body and no mutable-content
                    // iOS applies the count itself - no banner, no app wake, no service extension.
                    ["apns-push-type"] = "alert",
                    ["apns-priority"] = "5",
                    // A burst of reads is one badge value, not a queue of them.
                    ["apns-collapse-id"] = "badge",
                },
                Aps = new Aps {
                    Badge = badgeCount,
                },
            },
        };
        var batchResponse = await FirebaseMessaging
            .SendEachForMulticastAsync(multicastMessage, cancellationToken)
            .ConfigureAwait(false);
        await HandleBatchResponse(batchResponse, deviceIds, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendSpeechStartedWake(
        ChatId chatId,
        AuthorId authorId,
        Moment startedAt,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        if (deviceIds.Count == 0)
            return;

        var data = new Dictionary<string, string>() {
            { Constants.Notification.MessageDataKeys.Kind, NotificationKind.SpeechStarted.ToString() },
            { Constants.Notification.MessageDataKeys.ChatId, chatId.Value },
            { Constants.Notification.MessageDataKeys.AuthorId, authorId.Value },
            {
                Constants.Notification.MessageDataKeys.Timestamp,
                ((long)startedAt.EpochOffset.TotalMilliseconds).ToString()
            },
        };
        var multicastMessage = new MulticastMessage {
            Tokens = deviceIds.Select(id => id.Value).ToList(),
            Data = data,
            // Android-only data message: a wake for stale speech is useless, so the short
            // TTL + per-chat collapse key keep at most the latest wake queued per device.
            Android = new AndroidConfig {
                Data = data,
                Priority = Priority.High,
                TimeToLive = TimeSpan.FromSeconds(60),
                CollapseKey = $"speech-started-{chatId.Value}",
            },
        };
        var batchResponse = await FirebaseMessaging
            .SendEachForMulticastAsync(multicastMessage, cancellationToken)
            .ConfigureAwait(false);
        Log.LogInformation(
            "SpeechStarted wake for chat '{ChatId}': FCM accepted {SuccessCount}/{Total}",
            chatId, batchResponse.SuccessCount, deviceIds.Count);
        await HandleBatchResponse(batchResponse, deviceIds, cancellationToken).ConfigureAwait(false);
    }

    // Protected/internal methods

    // It's internal to be accessible from tests
    internal static List<DismissalChunk> ChunkDismissals(IReadOnlyCollection<PendingDismissal> dismissals)
    {
        // Over the FCM payload limit the whole message is rejected, taking every dismissal in it
        // down. A chunk pays for both keys it emits - one id per dismissal, one tag per distinct
        // tag - so the two stay consistent and a rejected chunk leaves exactly its own owed.
        var budget = MaxFcmPayloadBytes - FcmPayloadMargin
            - Encoding.UTF8.GetByteCount(Constants.Notification.MessageDataKeys.DismissedIds)
            - Encoding.UTF8.GetByteCount(Constants.Notification.MessageDataKeys.DismissedTags);
        var chunks = new List<DismissalChunk>();
        // Only chat/entry-derived tags are emitted: a client closes every notification sharing
        // a tag, so the non-chat "topic" fallback must never be a dismissal tag. An untagged
        // dismissal closes no banner, but its id still refreshes the badge.
        var items = dismissals.Where(x => x.Tag.IsNullOrEmpty()).ToList();
        var tags = new List<string>();
        var size = items.Sum(IdSize);
        foreach (var group in dismissals.Where(x => !x.Tag.IsNullOrEmpty()).GroupBy(x => x.Tag)) {
            var groupSize = Encoding.UTF8.GetByteCount(group.Key) + 1 + group.Sum(IdSize);
            if (tags.Count > 0 && size + groupSize > budget) {
                chunks.Add(new DismissalChunk(items, tags));
                items = [];
                tags = [];
                size = 0;
            }
            items.AddRange(group);
            tags.Add(group.Key);
            size += groupSize;
        }
        if (items.Count > 0 || tags.Count > 0)
            chunks.Add(new DismissalChunk(items, tags));

        return chunks;

        static int IdSize(PendingDismissal dismissal)
            // + the ',' separator
            => Encoding.UTF8.GetByteCount(dismissal.Id.Value) + 1;
    }

    // Private methods

    private async Task HandleBatchResponse(
        BatchResponse batchResponse,
        IReadOnlyCollection<Symbol> deviceIds,
        CancellationToken cancellationToken)
    {
        if (DebugLog != null) {
            var messageIds = string.Join(", ",
                batchResponse.Responses.Select(c =>
                    c.IsSuccess
                        ? c.MessageId
                        : c.Exception.MessagingErrorCode.HasValue
                            ? "errCode=" + c.Exception.MessagingErrorCode
                            : c.Exception.Message));
            DebugLog.LogDebug("Sent {Successfully}/{Total} messages. Result: '{MessageIds}'",
                batchResponse.SuccessCount, batchResponse.Responses.Count, messageIds);
        }

        if (batchResponse.FailureCount <= 0)
            return;

        var responses = batchResponse.Responses
            .Zip(deviceIds)
            .Select(p => new {
                DeviceId = p.Second,
                p.First.IsSuccess,
                p.First.Exception?.MessagingErrorCode,
                p.First.Exception?.HttpResponse,
            })
            .ToList();
        var responseGroups = responses
            .GroupBy(x => x.MessagingErrorCode);
        foreach (var responseGroup in responseGroups)
            if (responseGroup.Key is MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch) {
                var tokensToRemove = responseGroup
                    .Select(g => g.DeviceId)
                    .ToArray();
                _ = Commander.Start(
                    new NotificationsBackend_RemoveDevices(tokensToRemove), true, CancellationToken.None);
            }
            else if (responseGroup.Key.HasValue) {
                var firstErrorItem = responseGroup.First();
                var errorContent = firstErrorItem.HttpResponse == null
                    ? ""
                    : await firstErrorItem.HttpResponse.Content
                        .ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
                Log.LogWarning("Notification messages were not sent. ErrorCode = {ErrorCode}; "
                    + "Count = {ErrorCount}; {Details}",
                    responseGroup.Key, responseGroup.Count(), errorContent);
            }
    }

    private static bool IsAccepted(BatchResponse batchResponse)
        // An unregistered token isn't a failure worth retrying - that device is gone and gets removed.
        => batchResponse.Responses.All(x =>
            x.IsSuccess
            || x.Exception.MessagingErrorCode
                is MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch);

    // Nested types

    internal sealed record DismissalChunk(
        IReadOnlyList<PendingDismissal> Dismissals,
        IReadOnlyList<string> Tags);
}
