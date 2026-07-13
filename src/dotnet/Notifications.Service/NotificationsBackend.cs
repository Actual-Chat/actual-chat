using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using ActualChat.Contacts;
using ActualChat.Db;
using ActualChat.Flows;
using ActualChat.Notifications.Db;
using ActualChat.Notifications.Module;
using ActualChat.Queues;
using ActualChat.Sharding;
using ActualChat.Users;
using ActualLab.Diagnostics;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace ActualChat.Notifications;

/// <summary>
/// Backend service implementation for managing push notifications and device tokens.
/// </summary>
public class NotificationsBackend(IServiceProvider services)
    : ShardedDbServiceBase<NotificationDbContext>(services), INotificationsBackend
{
    private const int WakePendingCapacity = 10_000;

    // Per-user soft-update buffers, owned by this shard. Entries are lost on restart by design
    // (see docs/plans/notif-api.md); a committed hard update always re-reads from the DB.
    private readonly ConcurrentDictionary<UserId, SoftBuffer> _softBuffers = new();
    // Suppresses duplicate wakes per (user, chat) while a just-sent wake is presumed in flight.
    // Not thread-safe by itself - always access under lock.
    private readonly RecentlySeenMap<(UserId UserId, ChatId ChatId), Unit> _wakePending = new(
        WakePendingCapacity,
        services.GetRequiredService<NotificationsSettings>().WalkieTalkieWakeTtl);

    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private Streaming.ILiveSessionsBackend LiveSessionsBackend { get; }
        = services.GetRequiredService<Streaming.ILiveSessionsBackend>();
    private IChatThreadsBackend ChatThreadsBackend { get; } = services.GetRequiredService<IChatThreadsBackend>();
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();
    private IChatPositionsBackend ChatPositionsBackend { get; } = services.GetRequiredService<IChatPositionsBackend>();
    private IUserPresencesBackend UserPresencesBackend { get; } = services.GetRequiredService<IUserPresencesBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private NotificationsSettings Settings { get; } = services.GetRequiredService<NotificationsSettings>();
    private IServerFeatures ServerFeatures { get; } = services.GetRequiredService<IServerFeatures>();
    private IDbEntityResolver<string, DbExplicitNotification> DbExplicitNotificationResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbExplicitNotification>>();

    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
        = services.KeyedFactory<IBackendChatMarkupHub, ChatId>();
    private IFirebaseMessagingClient FirebaseMessagingClient { get; }
        = services.GetRequiredService<IFirebaseMessagingClient>();
    private IApnsClient ApnsClient { get; } = services.GetRequiredService<IApnsClient>();
    private FlowHub FlowHub { get; } = services.FlowHub();
    private IQueues Queues { get; } = services.Queues();
    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private CancellationToken StopToken { get; } = services.GetService<IHostApplicationLifetime>().StopToken();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.Notifications);

    // [ComputeMethod]
    public virtual async Task<ExplicitNotification?> GetExplicit(
        ExplicitNotificationId notificationId,
        CancellationToken cancellationToken)
    {
        var dbNotification = await DbExplicitNotificationResolver
            .Get(notificationId.Value, cancellationToken)
            .ConfigureAwait(false);
        return dbNotification?.ToModel();
    }

    // [ComputeMethod]
    public virtual Task<IReadOnlyList<Device>> ListDevices(UserId userId, CancellationToken cancellationToken)
        => ListDevices(userId, Symbol.Empty, null, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<IReadOnlyList<UserId>> ListSubscribedUserIds(
        ChatId chatId, NotificationImportance importance, CancellationToken cancellationToken)
    {
        if (chatId.IsThread(out var threadChatId)) {
            var subscriberIds = await ListSubscribedUserIds(threadChatId.ParentChatId, importance, cancellationToken)
                .ConfigureAwait(false);
            subscriberIds = await FilterByFollowThreadStatus(subscriberIds, chatId, cancellationToken)
                .ConfigureAwait(false);
            subscriberIds = await FilterByNotificationMode(subscriberIds, chatId, importance, cancellationToken)
                .ConfigureAwait(false);
            return subscriberIds;
        }
        else {
            var subscriberIds = await AuthorsBackend.ListUserIds(chatId, cancellationToken).ConfigureAwait(false);
            subscriberIds = await FilterByNotificationMode(subscriberIds, chatId, importance, cancellationToken)
                .ConfigureAwait(false);
            return subscriberIds;
        }
    }

    // [ComputeMethod]
    public virtual async Task<UserNotificationInfo> GetUserNotificationInfo(
        UserId userId,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbUserNotifications = await dbContext.UserNotifications
            .FirstOrDefaultAsync(x => x.Id == userId.Value, cancellationToken)
            .ConfigureAwait(false);
        var info = dbUserNotifications?.ToModel() ?? new UserNotificationInfo(userId);

        // Population re-check: hide notifications the user has since read, or that the chat's
        // notification mode suppresses (the mode may change after the notification was shown;
        // ringer kinds stay visible even when muted). This is the single source of truth for the
        // "active" set — the in-app list, the badge count, and the client reconciler all agree,
        // and OnPush drops sends for anything hidden here, so fan-out and this filter must use
        // the same NotificationHelper predicates.
        // Reads IChatPositionsBackend.Get + notification mode (once per distinct chat), so Fusion
        // re-invalidates this method whenever a read position or mode setting changes.
        var readPositions = await GetReadPositions(userId, info.Displayed, cancellationToken).ConfigureAwait(false);
        var modes = await GetChatNotificationModes(userId, info.Displayed, cancellationToken).ConfigureAwait(false);
        var displayed = info.Displayed.Without(n => IsRead(n, readPositions) || IsSuppressedByMode(n, modes));
        if (displayed.Count == info.Displayed.Count)
            return info;

        return info with {
            Displayed = displayed,
            IsDormant = displayed.Count >= Constants.Notification.DormancyThreshold,
        };
    }

    // [CommandHandler]
    public virtual async Task OnNotify(
        NotificationsBackend_Notify command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // GetUserNotificationInfo is invalidated by ApplyHardUpdate's completion handler

        var notification = command.Notification;
        var userId = notification.UserId.Require();

        DebugLog?.LogDebug("-> OnNotify. UserId={UserId}, NotificationId={NotificationId}",
            userId, notification.Id);

        var info = await GetUserNotificationInfo(userId, cancellationToken).ConfigureAwait(false);
        if (info.IsDormant) {
            DebugLog?.LogDebug("OnNotify: skipped (dormant). UserId={UserId}, NotificationId={NotificationId}",
                userId, notification.Id);
            return;
        }

        if (notification.SentAt == default) {
            // Reuse an already-displayed notification's SentAt so a SentAt-less redelivery stays a
            // no-op (MergeWith treats an equal SentAt as a duplicate) instead of re-alerting; only a
            // genuinely first-seen notification is stamped Now.
            var displayed = info.Displayed.FirstOrDefault(n => n.Id == notification.Id);
            notification = notification with { SentAt = displayed?.SentAt ?? Clocks.SystemClock.Now };
        }

        if (IsSoftUpdate(info, notification)) {
            EnqueueSoft(userId, notification, info);
            return;
        }

        if (await ShouldDeferForActiveReader(userId, notification, cancellationToken).ConfigureAwait(false)) {
            DebugLog?.LogDebug("OnNotify: deferred (active reader). UserId={UserId}, Id={NotificationId}",
                userId, notification.Id);
            EnqueueSoft(userId, notification, info, Constants.Notification.ActiveReaderGrace);
            return;
        }

        var batch = DrainSoftBuffer(userId);
        batch.Add(notification);
        await ApplyHardUpdate(userId, batch, [], cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnProcess(
        NotificationsBackend_Process command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // GetUserNotificationInfo is invalidated by ApplyHardUpdate's completion handler

        var userId = command.UserId;
        var batch = DrainSoftBuffer(userId);
        if (batch.Count == 0)
            return;

        DebugLog?.LogDebug("-> OnProcess. UserId={UserId}, Count={Count}", userId, batch.Count);
        await ApplyHardUpdate(userId, batch, [], cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnHandle(
        NotificationsBackend_Handle command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // GetUserNotificationInfo is invalidated by ApplyHardUpdate's completion handler

        var notificationId = command.NotificationId;
        DebugLog?.LogDebug("-> OnHandle. NotificationId={NotificationId}", notificationId);
        await ApplyHardUpdate(notificationId.UserId, [], [notificationId], cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnHandleAll(
        NotificationsBackend_HandleAll command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // GetUserNotificationInfo is invalidated by ApplyHardUpdate's completion handler

        var userId = command.UserId;
        DebugLog?.LogDebug("-> OnHandleAll. UserId={UserId}", userId);
        // handleAll dismisses the raw committed set (not the compute-filtered view), so
        // notifications hidden by a currently muted chat are dismissed too and can't resurface
        // as unread when the chat is unmuted.
        await ApplyHardUpdate(userId, [], [], handleAll: true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<bool> OnUpsertExplicitNotification(
        NotificationsBackend_UpsertExplicitNotification command,
        CancellationToken cancellationToken)
    {
        var notification = command.Notification;
        var sid = notification.Id.Value;

        if (Invalidation.IsActive) {
            // Created or Updated
            _ = GetExplicit(notification.Id, default);
            return default;
        }

        try {
            var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);

            var dbNotification = await dbContext.ExplicitNotifications.ForUpdate()
                .FirstOrDefaultAsync(e => e.Id == sid, cancellationToken)
                .ConfigureAwait(false);

            var now = Clocks.SystemClock.Now;
            if (dbNotification == null) {
                // Create
                notification = notification with {
                    Version = VersionGenerator.NextVersion(),
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                dbNotification = new DbExplicitNotification();
                dbNotification.UpdateFrom(notification);
                dbContext.ExplicitNotifications.Add(dbNotification);
            }
            else {
                // Update
                notification = notification with {
                    Version = VersionGenerator.NextVersion(notification.Version),
                    UpdatedAt = now
                };
                dbNotification.UpdateFrom(notification);
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException e) when(e.Entries.All(en => en.State == EntityState.Added)) {
            // Notification has already been created for another message, let's skip
            return false;
        }

        return true;
    }

    // [CommandHandler]
    public virtual async Task OnRegisterDevice(
        NotificationsBackend_RegisterDevice command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var device = context.Operation.Items.KeylessGet<DbDevice>();
            var isNew = context.Operation.Items.KeylessGet(false);
            if (isNew && device != null)
                _ = ListDevices(UserId.Parse(device.UserId), default);
            return;
        }

        var (userId, deviceId, deviceType, sessionHash) = command;
        DebugLog?.LogDebug("-> OnRegisterDevice. UserId={UserId}, DeviceId={DeviceId}, "
            + "DeviceType={DeviceType}, SessionHash={SessionHash}",
            userId, deviceId, deviceType, sessionHash);
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        var existingDbDevice = await dbContext.Devices.ForUpdate()
            .FirstOrDefaultAsync(d => d.Id == deviceId.Value, cancellationToken)
            .ConfigureAwait(false);

        var dbDevice = existingDbDevice;
        if (dbDevice == null) {
            dbDevice = new DbDevice {
                Id = deviceId,
                Type = deviceType,
                UserId = userId.Value,
                SessionHash = sessionHash,
                Version = VersionGenerator.NextVersion(),
                CreatedAt = Clocks.SystemClock.Now,
            };
            dbContext.Add(dbDevice);
        }
        else {
            DebugLog?.LogDebug("-- OnRegisterDevice. Existing DbDevice found:"
                + " UserId={UserId}, DeviceId={DeviceId}, DeviceType={DeviceType}, "
                + "SessionHash={SessionHash}, AccessedAt={AccessedAt}",
                dbDevice.UserId, dbDevice.Id, dbDevice.Type, dbDevice.SessionHash, dbDevice.AccessedAt);
            dbDevice.AccessedAt = Clocks.SystemClock.Now;
            if (dbDevice.Type == DeviceType.WebBrowser && deviceType != DeviceType.WebBrowser)
                dbDevice.Type = deviceType; // Now MAUI app reports device type properly, lets update it.
            if (dbDevice.SessionHash.IsNullOrEmpty() && !sessionHash.IsEmpty)
                dbDevice.SessionHash = sessionHash;
            if (UserId.TryParse(dbDevice.UserId, out var existingUserId) && existingUserId != userId) {
                if (existingUserId.IsGuest) {
                    dbDevice.UserId = userId.Value;
                    DebugLog?.LogDebug(
                        "Guest UserId for Device '{DeviceId}' has been updated: "
                        + "'{OldUserId}'->'{NewUserId}'",
                        existingUserId, existingUserId, userId);
                }
                else
                    Log.LogWarning("User {UserId} is trying to register device for {ExistingUserId}. Skipped",
                        userId, existingUserId);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(dbDevice);
        context.Operation.Items.KeylessSet(existingDbDevice == null);
    }

    // [CommandHandler]
    public virtual async Task OnRemoveDevices(
        NotificationsBackend_RemoveDevices command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invUserIds = context.Operation.Items.KeylessGet<HashSet<UserId>>();
            if (invUserIds is { Count: > 0 })
                foreach (var invUserId in invUserIds)
                    _ = ListDevices(invUserId, default);
            return;
        }

        var affectedUserIds = new HashSet<UserId>();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        foreach (var deviceId in command.DeviceIds) {
            var dbDevice = await dbContext.Devices
                .Get(deviceId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (dbDevice == null)
                continue;

            dbContext.Devices.Remove(dbDevice);
            affectedUserIds.Add(UserId.Parse(dbDevice.UserId));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("Removed {Count} devices", affectedUserIds.Count);
        context.Operation.Items.KeylessSet(affectedUserIds);
    }

    // [CommandHandler]
    public virtual async Task OnRemoveAccount(
        NotificationsBackend_RemoveAccount command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var userId = command.UserId;
        var context = CommandContext.GetCurrent();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        context.Operation.MustStore(false);

        var removedDeviceCount = await dbContext.Devices
            .Where(a => a.UserId == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await dbContext.UserNotifications
            .Where(a => a.Id == userId.Value)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        context.Operation.AddCompletionHandler(scope => {
            using (Invalidation.Begin()) {
                _ = GetUserNotificationInfo(userId, default);
                _ = ListDevices(userId, default);
            }
            return Task.CompletedTask;
        });
        Log.LogInformation("Removed account notification data: {DeviceCount} device(s), UserId={UserId}",
            removedDeviceCount, userId);
    }

    // [CommandHandler]
    public virtual async Task OnNotifyMembers(
        NotificationsBackend_NotifyMembers command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (userId, chatId, lastEntryLocalId) = command;
        // An explicit author action ("notify all") is a ringer: it breaks through chat mute.
        var userIds = await ListSubscribedUserIds(chatId, NotificationImportance.Ringer, cancellationToken)
            .ConfigureAwait(false);
        await NotifyMembersInternal(userId, chatId, lastEntryLocalId, userIds, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnNotifyMentionedMembers(
        NotificationsBackend_NotifyMentionedMembers command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (userId, ChatEntryId, mentionedUserIds) = command;
        var chatId = ChatEntryId.ChatId;

        // An explicit author action ("urgently notify mentioned members") is a ringer: it breaks
        // through chat mute — unlike plain in-text mentions, which ride the Message fan-out.
        var subscribedUserIds = await ListSubscribedUserIds(chatId, NotificationImportance.Ringer, cancellationToken)
            .ConfigureAwait(false);
        var userIds = subscribedUserIds.Intersect(mentionedUserIds).ToArray();
        if (userIds.Length == 0)
            return;

        await NotifyMembersInternal(userId, chatId, ChatEntryId.LocalId, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnNotifyConversation(
        NotificationsBackend_NotifyConversation command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (conversationId, phase, text, endEntryLid, authorIds) = command;
        var chatId = conversationId.ChatId;
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return;

        var authorUserIds = new HashSet<UserId>();
        AuthorFull? primaryAuthor = null;
        var authorNames = new List<string>();
        foreach (var authorId in authorIds) {
            var author = await AuthorsBackend.Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken)
                .ConfigureAwait(false);
            if (author is null)
                continue;

            primaryAuthor ??= author;
            authorUserIds.Add(author.UserId);
            authorNames.Add(author.Avatar.Name);
        }
        var iconUrl = primaryAuthor is null ? "" : NotificationHelper.GetIconUrl(chat, primaryAuthor, UrlMapper);

        // The Started banner names who opened the voice chat instead of the generic phrase.
        if (phase == ConversationNotificationPhase.Started && authorNames.Count > 0)
            text = NotificationHelper.GetVoiceChatStartedText(authorNames);

        // Started/Titled/Final are live phases; their joined participants already see the call live.
        var isLive = phase != ConversationNotificationPhase.Created;
        var activeUserIds = isLive
            ? await GetActiveParticipantUserIds(chatId, cancellationToken).ConfigureAwait(false)
            : new HashSet<UserId>();
        var userIds = await ListSubscribedUserIds(chatId, NotificationImportance.Ordinary, cancellationToken)
            .ConfigureAwait(false);
        var now = Clocks.CoarseSystemClock.Now;
        foreach (var userId in userIds) {
            if (authorUserIds.Contains(userId))
                continue;
            if (activeUserIds.Contains(userId))
                continue;

            var notification = ConversationNotification.New(userId, conversationId, endEntryLid) with {
                Title = chat.Title,
                Text = text,
                IconUrl = iconUrl,
                SentAt = now,
            };
            await Queues.Enqueue(new NotificationsBackend_Notify(notification), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<HashSet<UserId>> GetActiveParticipantUserIds(
        ChatId chatId, CancellationToken cancellationToken)
    {
        // The live-session backend tracks participants by AuthorId; notifications target users, so resolve
        // the chat-scoped authors to their user ids here (anonymous, user-less authors simply drop out).
        var authorIds = await LiveSessionsBackend
            .ListParticipants(chatId, cancellationToken)
            .ConfigureAwait(false);
        var userIds = new HashSet<UserId>();
        foreach (var authorId in authorIds) {
            var author = await AuthorsBackend
                .Get(chatId, authorId, RequestedAuthorKind.Default, cancellationToken)
                .ConfigureAwait(false);
            if (author is { } a && !a.UserId.Value.IsNullOrEmpty())
                userIds.Add(a.UserId);
        }
        return userIds;
    }

    [CommandHandler]
    public virtual async Task OnNotifyCall(
        NotificationsBackend_NotifyCall command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (conversationId, caller, invitees, hasVideo) = command;
        var chatId = conversationId.ChatId;
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return;

        var callerAuthor = await AuthorsBackend
            .Get(chatId, caller, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        var iconUrl = callerAuthor is null ? "" : NotificationHelper.GetIconUrl(chat, callerAuthor, UrlMapper);
        var text = hasVideo ? "Incoming video call" : "Incoming call";
        var now = Clocks.CoarseSystemClock.Now;
        // Unlike a conversation notification, the ring targets the invitees themselves.
        foreach (var inviteeAuthorId in invitees) {
            var invitee = await AuthorsBackend
                .Get(chatId, inviteeAuthorId, RequestedAuthorKind.Default, cancellationToken)
                .ConfigureAwait(false);
            if (invitee is not { } a || a.UserId.Value.IsNullOrEmpty())
                continue;

            var notification = CallNotification.New(a.UserId, conversationId, caller, hasVideo) with {
                Title = chat.Title,
                Text = text,
                IconUrl = iconUrl,
                SentAt = now,
            };
            await Queues.Enqueue(new NotificationsBackend_Notify(notification), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    [CommandHandler]
    public virtual async Task OnCancelCall(
        NotificationsBackend_CancelCall command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (conversationId, invitees) = command;
        var chatId = conversationId.ChatId;
        foreach (var inviteeAuthorId in invitees) {
            var invitee = await AuthorsBackend
                .Get(chatId, inviteeAuthorId, RequestedAuthorKind.Default, cancellationToken)
                .ConfigureAwait(false);
            if (invitee is not { } a || a.UserId.Value.IsNullOrEmpty())
                continue;

            // Same id as the ring (keyed by the call's ConversationId): handling it drops the ring
            // from the active set (so it can't linger in ListActive / the in-app list) and closes
            // the device banner via the dismissal push ApplyHardUpdate emits for a removed notification.
            var notificationId = NotificationId.New(a.UserId, NotificationKind.IncomingCall, conversationId.Value);
            await Queues
                .Enqueue(new NotificationsBackend_Handle(notificationId), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // Event handlers

    // [EventHandler]
    public virtual async Task OnChatEntryChangedEvent(
        ChatEntryChangedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (entry, author, changeKind, oldEntry) = eventCommand;
        if (entry.IsSystemEntry)
            return;

        if (!ShouldNotify(entry, oldEntry, changeKind))
            return;

        // Suppress per-message notifications for a session participant's own entries inside a
        // latched live session — the call's own transcript. Solo (pre-latch) transcription and
        // non-participants' messages typed during the call still notify normally.
        var live = await LiveSessionsBackend.GetState(entry.ChatId, cancellationToken).ConfigureAwait(false);
        if (live is { SessionStartedAt: not null } lc
            && entry.LocalId >= lc.StartEntryLid && lc.AuthorIds.Contains(entry.AuthorId))
            return;

        await SendChatMessageNotification(entry, author, cancellationToken).ConfigureAwait(false);
        return;

        // - plain typed text: Create already carries the full content → push immediately;
        // - audio / JustText voice: Create is empty + streaming, final content arrives only
        //   in the subsequent Update → push on the streaming → finalized transition.
        static bool ShouldNotify(ChatEntry entry, ChatEntry? oldEntry, ChangeKind changeKind) {
            if (changeKind == ChangeKind.Create)
                return !entry.IsContentStreaming;
            if (changeKind != ChangeKind.Update || oldEntry is null)
                return false;
            return oldEntry.IsContentStreaming && !entry.IsContentStreaming;
        }
    }

    // [EventHandler]
    public virtual async Task OnReactionChangedEvent(
        ReactionChangedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (reaction, entry, author, reactionAuthor, changeKind) = eventCommand;
        if (changeKind == ChangeKind.Remove)
            return;
        if (author.UserId.IsGuest) // No notifs for guests
            return;
        if (author.Id == reactionAuthor.Id) // No notifs on your own reactions to your own messages
            return;

        // Reactions bypass the subscriber fan-out (they target the message author directly), so the
        // notification mode is checked here; they're Ordinary — delivered only in Default mode.
        var mode = await GetNotificationMode(author.UserId, entry.ChatId, cancellationToken).ConfigureAwait(false);
        if (!NotificationHelper.IsDeliverable(NotificationImportance.Ordinary, mode))
            return;

        var (text, _) = await NotificationHelper
            .GetText(entry, MarkupConsumer.ReactionNotification, ChatMarkupHubFactory, cancellationToken)
            .ConfigureAwait(false);
        if (!entry.Content.IsNullOrEmpty())
            text = $"\"{text}\"";
        text = $"{reaction.Emoji} to {text}";
        var userIds = new[] { author.UserId };
        var similarityKey = entry.ChatId.Value;
        await EnqueueMessageRelatedNotifications(
            entry.ChatId, entry.Id, reactionAuthor, text, NotificationKind.Reaction,
            similarityKey, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    [EventHandler]
    public virtual async Task OnChatChangedEventEvent(
        ChatChangedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (chat, _, changeKind) = eventCommand;
        if (!(changeKind is ChangeKind.Create && chat.Id.IsThread(out var threadChatId)))
            return;

        // New thread has been created.
        var parentChatId = threadChatId.ParentChatId;
        var userIds = await ListSubscribedUserIds(parentChatId, NotificationImportance.Ordinary, cancellationToken)
            .ConfigureAwait(false);
        var similarityKey = parentChatId.Value;
        var creator = await ChatThreadsBackend.GetThreadCreator(chat.Id, cancellationToken).ConfigureAwait(false);
        if (creator is null)
            return;

        var text = $"Thread '{chat.Title}' has been created";
        await EnqueueMessageRelatedNotifications(
                parentChatId, null, creator, text, NotificationKind.Thread, similarityKey, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnConversationChangedEvent(
        ConversationChangedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (conversation, _, changeKind, suppressNotification) = eventCommand;
        if (suppressNotification || changeKind == ChangeKind.Remove)
            return; // A live conversation's materialization is already covered by its Final notification

        var command = new NotificationsBackend_NotifyConversation(
            conversation.Id,
            ConversationNotificationPhase.Created,
            conversation.Title,
            conversation.EndEntryLid,
            conversation.AuthorIds);
        await Queues.Enqueue(command, cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    // Reconciles read notifications promptly when a Read position advances, so a notification
    // read on one device is dropped (and a silent dismissal pushed) on the others without waiting
    // for the user's next notification event.
    public virtual async Task OnReadPositionChangedEvent(
        ReadPositionChangedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (userId, chatId, _) = eventCommand;

        // Cheap gate: avoid the operation + row lock + push path unless this user has a displayed
        // notification anchored to this chat. The event's EntryLid is advisory (collapsed events
        // keep the window's first advance) — ApplyHardUpdate re-checks the live Read position.
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        var dbUserNotifications = await dbContext.UserNotifications
            .FirstOrDefaultAsync(x => x.Id == userId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbUserNotifications is null)
            return;

        var hasNotificationInChat = dbUserNotifications.ToModel().Displayed.Any(n => {
            var (anchorChatId, anchorEntryLid) = GetReadAnchor(n);
            return anchorChatId == chatId && anchorEntryLid > 0;
        });
        if (!hasNotificationInChat)
            return;

        DebugLog?.LogDebug("-> OnReadPositionChangedEvent. UserId={UserId}, ChatId={ChatId}", userId, chatId);
        await ApplyHardUpdate(userId, [], [], cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnSignedOut(
        UserSignedOutEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var session = eventCommand.Session;
        var devices = await ListDevices(eventCommand.UserId, session.Hash, null, cancellationToken)
            .ConfigureAwait(false);
        if (devices.Count == 0)
            return;

        var command = new NotificationsBackend_RemoveDevices(devices.Select(c => c.DeviceId).ToArray());
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [EventHandler]
    public virtual async Task OnSpeechStartedEvent(SpeechStartedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (chatId, authorId, startedAt) = eventCommand;
        if (!await ServerFeatures.Get<Features_EnableWalkieTalkiePush>(cancellationToken).ConfigureAwait(false))
            return;

        var userIds = await AuthorsBackend.ListUserIds(chatId, cancellationToken).ConfigureAwait(false);
        if (userIds.Length > Settings.WalkieTalkieMaxChatMembers)
            return;

        var speaker = await AuthorsBackend
            .Get(chatId, authorId, RequestedAuthorKind.Default, cancellationToken)
            .ConfigureAwait(false);
        var activeUserIds = await GetActiveParticipantUserIds(chatId, cancellationToken).ConfigureAwait(false);
        foreach (var userId in userIds) {
            if (userId == speaker?.UserId || activeUserIds.Contains(userId))
                continue;

            try {
                await SendWalkieTalkieWake(userId, chatId, authorId, startedAt, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e,
                    "Walkie-talkie wake failed for user '{UserId}' in chat '{ChatId}'", userId, chatId);
            }
        }
    }

    // Private methods

    private async Task SendChatMessageNotification(
        ChatEntry entry,
        AuthorFull author,
        CancellationToken cancellationToken)
    {
        var freshEntry = await ChatsBackend
            .GetEntry(entry.Id, Constants.Notification.EntryWaitTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (freshEntry is null)
            return;

        var entryId = freshEntry.Id;
        var chatId = entryId.ChatId;
        var (text, mentionIds) = await NotificationHelper
            .GetText(freshEntry, MarkupConsumer.Notification, ChatMarkupHubFactory, cancellationToken)
            .ConfigureAwait(false);
        // The mode != Muted superset; ImportantOnly users must survive until the split below.
        var userIds = await ListSubscribedUserIds(chatId, NotificationImportance.Important, cancellationToken)
            .ConfigureAwait(false);
        // Don't interrupt users who are actively in this chat's live call — they're present.
        var live = await LiveSessionsBackend.GetState(chatId, cancellationToken).ConfigureAwait(false);
        if (live is { SessionStartedAt: not null }) {
            var active = await GetActiveParticipantUserIds(chatId, cancellationToken).ConfigureAwait(false);
            if (active.Count != 0)
                userIds = userIds.Where(x => !active.Contains(x)).ToList();
        }

        // Users personally @mentioned in the text get a Mention notification (delivered under
        // ImportantOnly, individually per entry); everyone else gets the plain coalescing Message
        // notification (Default mode only).
        var mentionedUserIds = await GetMentionedUserIds(chatId, mentionIds, cancellationToken).ConfigureAwait(false);
        var mentioned = userIds.Where(mentionedUserIds.Contains).ToList();
        if (mentioned.Count > 0)
            await EnqueueMessageRelatedNotifications(
                chatId, entryId, author, text, NotificationKind.Mention,
                entryId.Value, mentioned, cancellationToken)
                .ConfigureAwait(false);

        var others = userIds.Where(x => !mentionedUserIds.Contains(x)).ToList();
        var ordinaryUserIds = await FilterByNotificationMode(
            others, chatId, NotificationImportance.Ordinary, cancellationToken)
            .ConfigureAwait(false);
        await EnqueueMessageRelatedNotifications(
            chatId, entryId, author, text, NotificationKind.Message,
            chatId.Value, ordinaryUserIds, cancellationToken)
            .ConfigureAwait(false);
    }

    // Resolves personal (author/user) mentions to user ids; chat/place/emoji mention kinds carry
    // no @all semantics here yet, so they're ignored.
    private async Task<HashSet<UserId>> GetMentionedUserIds(
        ChatId chatId, IReadOnlyCollection<MentionRef> mentionIds, CancellationToken cancellationToken)
    {
        var userIds = new HashSet<UserId>();
        foreach (var mention in mentionIds)
            switch (mention.Target) {
            case UserId userId:
                userIds.Add(userId);
                break;
            case AuthorId authorId: {
                var mentionedAuthor = await AuthorsBackend
                    .Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken)
                    .ConfigureAwait(false);
                if (mentionedAuthor is { } a && !a.UserId.Value.IsNullOrEmpty())
                    userIds.Add(a.UserId);
                break;
            }
            }
        return userIds;
    }

    // [CommandHandler]
    // The actual FCM send, run as a queued command so NATS retries it on transient failure and
    // it isn't lost if the pushing node dies mid-send.
    public virtual async Task OnPush(
        NotificationsBackend_Push command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // No state change, nothing to invalidate

        var notification = command.Notification;
        var userId = notification.UserId.Require();

        // Re-read current state at delivery: skip if the notification is no longer active (read,
        // handled, or muted since it was enqueued), and take the badge from the current active
        // set — so a redelivered/out-of-order queue message can't resurrect it or stamp a stale badge.
        var info = await GetUserNotificationInfo(userId, cancellationToken).ConfigureAwait(false);
        // Send the converged displayed notification (not the enqueued one), so aggregated content,
        // the first-unread anchor and the beep state all reflect the committed state.
        var displayed = info.Displayed.FirstOrDefault(n => n.Id == notification.Id);
        if (displayed is null)
            return;

        notification = displayed;
        var minActiveAt = Clocks.SystemClock.Now - Constants.Notification.ActiveDevicePeriod;
        var devices = await ListDevices(userId, Symbol.Empty, minActiveAt, cancellationToken).ConfigureAwait(false);
        devices = devices.Where(d => d.DeviceType != DeviceType.iOSPttApp).ToList();
        if (devices.Count == 0) {
            DebugLog?.LogDebug("No recipient devices found for notification #{NotificationId}",
                notification.Id);
            return;
        }

        var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        var isAdmin = account is { IsAdmin: true };
        var deviceIds = devices.Select(d => d.DeviceId).ToList();
        var entryId = GetEntryId(notification);
        DebugLog?.LogDebug("-> OnPush. EntryId={EntryId}, UserId={UserId}, NotificationId={NotificationId}, "
            + "IsSilent={IsSilent}, DeviceIds#={DeviceIdCount}",
            entryId, userId, notification.Id, command.IsSilent, deviceIds.Count);
        await FirebaseMessagingClient
            .SendMessage(notification, deviceIds, isAdmin, info.Displayed.Count, command.IsSilent, cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnPushDismissal(
        NotificationsBackend_PushDismissal command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // No state change, nothing to invalidate

        var (userId, dismissed) = command;
        var minActiveAt = Clocks.SystemClock.Now - Constants.Notification.ActiveDevicePeriod;
        var devices = await ListDevices(userId, Symbol.Empty, minActiveAt, cancellationToken).ConfigureAwait(false);
        devices = devices.Where(d => d.DeviceType != DeviceType.iOSPttApp).ToList();
        if (devices.Count == 0)
            return;

        // Badge recomputed from current state at delivery (see OnPush).
        var info = await GetUserNotificationInfo(userId, cancellationToken).ConfigureAwait(false);
        var deviceIds = devices.Select(d => d.DeviceId).ToList();
        DebugLog?.LogDebug("-> OnPushDismissal. UserId={UserId}, Notifications#={Count}, DeviceIds#={DeviceIdCount}",
            userId, dismissed.Count, deviceIds.Count);
        await FirebaseMessagingClient
            .SendDismissal(dismissed, deviceIds, info.Displayed.Count, cancellationToken)
            .ConfigureAwait(false);
    }

    // Fetches each distinct chat's Read position once (in parallel) instead of one sequential
    // round-trip per notification. Read-state is then evaluated per notification *instance*
    // against its own EntryLid — two notifications can share a NotificationId (chat-keyed dedup)
    // yet anchor to different entries.
    private async Task<IReadOnlyDictionary<ChatId, long>> GetReadPositions(
        UserId userId, IEnumerable<Notification> notifications, CancellationToken cancellationToken)
    {
        var chatIds = notifications
            .Select(n => GetReadAnchor(n).ChatId)
            .Where(c => c is not null)
            .Distinct()
            .ToList();
        if (chatIds.Count == 0)
            return ReadOnlyDictionary<ChatId, long>.Empty;

        var positions = await chatIds
            .Select(async chatId => (
                ChatId: chatId!,
                ReadEntryLid: (await ChatPositionsBackend
                    .Get(userId, chatId!, ChatPositionKind.Read, cancellationToken)
                    .ConfigureAwait(false)).EntryLid))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return positions.ToDictionary(x => x.ChatId, x => x.ReadEntryLid);
    }

    // A chat notification is read once the user's Read position has advanced past its entry.
    private static bool IsRead(Notification notification, IReadOnlyDictionary<ChatId, long> readPositions)
    {
        var (chatId, entryLid) = GetReadAnchor(notification);
        if (chatId is null || entryLid <= 0)
            return false;
        if (!readPositions.TryGetValue(chatId, out var readEntryLid))
            return false;
        // A read position of 0 means "never read"; long.MaxValue is the client's "unbounded"
        // sentinel that must never gate a real entry (it would suppress every notification in the
        // chat forever). Treat both as "not read".
        if (readEntryLid is <= 0 or long.MaxValue)
            return false;
        return readEntryLid >= entryLid;
    }

    private async Task<bool> ShouldDeferForActiveReader(
        UserId userId, Notification notification, CancellationToken cancellationToken)
    {
        // Holds a plain chat message instead of alerting when the present recipient is already caught
        // up to the chat's head: they're actively reading, so it's likely read within the grace window
        // and then dropped silently on every device. Mentions/replies/ringers, users not caught up, and
        // absent users all alert immediately. No message is lost — an unread one alerts after the grace
        // window via the deferred re-check (Reconcile drops it only once actually read).
        // Restricted to chat-message notifications: reactions and conversation/attention signals are
        // Ordinary too but aren't "messages the reader is about to scroll past", so they alert as usual.
        if (notification is not ChatEntryRelatedNotification
            || NotificationHelper.GetImportance(notification.Kind) != NotificationImportance.Ordinary)
            return false;

        var (chatId, entryLid) = GetReadAnchor(notification);
        if (chatId is null || entryLid <= 0)
            return false;

        var readEntryLid = (await ChatPositionsBackend
            .Get(userId, chatId, ChatPositionKind.Read, cancellationToken)
            .ConfigureAwait(false)).EntryLid;
        // long.MaxValue is the client's "unbounded" read sentinel; caught up == read the entry just
        // before this one (readEntryLid + 1 >= entryLid), tolerating the common consecutive-lid case.
        if (readEntryLid is <= 0 or long.MaxValue || readEntryLid + 1 < entryLid)
            return false;

        var lastCheckIn = await UserPresencesBackend.GetLastCheckIn(userId, cancellationToken).ConfigureAwait(false);
        return lastCheckIn is { } at && Clocks.SystemClock.Now - at <= Constants.Presence.AwayTimeout;
    }

    // Returns the notification mode of the notifications' chats, read once per distinct chat
    // (in parallel).
    private async Task<Dictionary<ChatId, ChatNotificationMode>> GetChatNotificationModes(
        UserId userId, IEnumerable<Notification> notifications, CancellationToken cancellationToken)
    {
        var chatIds = notifications
            .Select(n => GetReadAnchor(n).ChatId)
            .Where(c => c is not null)
            .Distinct()
            .ToList();
        if (chatIds.Count == 0)
            return [];

        var modeByChat = await chatIds
            .Select(async chatId => (
                ChatId: chatId!,
                Mode: await GetNotificationMode(userId, chatId!, cancellationToken).ConfigureAwait(false)))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        return modeByChat.ToDictionary(x => x.ChatId, x => x.Mode);
    }

    // Mentions and explicit pings share NotificationKind.Attention, so a mention displayed before
    // the user muted the chat stays visible (treated as Ringer); new ones are dropped at fan-out.
    private static bool IsSuppressedByMode(Notification notification, Dictionary<ChatId, ChatNotificationMode> modes)
    {
        var chatId = GetReadAnchor(notification).ChatId;
        if (chatId is null || !modes.TryGetValue(chatId, out var mode))
            return false;

        return !NotificationHelper.IsDeliverable(NotificationHelper.GetImportance(notification.Kind), mode);
    }

    private async Task<ChatNotificationMode> GetNotificationMode(
        UserId userId,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var kvas = ServerKvasBackend.ForUser(userId);
        return await kvas.ChatUserSettings(chatId)
            .Get(x => x.NotificationMode, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendWalkieTalkieWake(
        UserId userId, ChatId chatId, AuthorId authorId, Moment startedAt, CancellationToken cancellationToken)
    {
        if (!await IsArmedForWalkieTalkie(userId, chatId, cancellationToken).ConfigureAwait(false))
            return;

        lock (_wakePending) {
            if (!_wakePending.TryAdd((userId, chatId)))
                return;
        }

        var devices = await ListDevices(userId, cancellationToken).ConfigureAwait(false);
        var fcmDeviceIds = devices
            .Where(d => d.DeviceType == DeviceType.AndroidApp)
            .Select(d => d.DeviceId)
            .ToList();
        if (fcmDeviceIds.Count != 0)
            await FirebaseMessagingClient
                .SendSpeechStartedWake(chatId, authorId, startedAt, fcmDeviceIds, cancellationToken)
                .ConfigureAwait(false);

        var pttDeviceIds = devices
            .Where(d => d.DeviceType == DeviceType.iOSPttApp)
            .Select(d => d.DeviceId)
            .ToList();
        if (pttDeviceIds.Count != 0) {
            // The PTT system UI needs a channel/speaker label at push time, before any RPC.
            var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
            await ApnsClient
                .SendPushToTalkWake(chatId, startedAt, chat?.Title.NullIfEmpty() ?? "Voxt", pttDeviceIds, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> IsArmedForWalkieTalkie(UserId userId, ChatId chatId, CancellationToken cancellationToken)
    {
        var kvas = ServerKvasBackend.ForUser(userId);
        var alwaysListened = await kvas.UserListeningSettings()
            .Get(x => x.AlwaysListenedChatIds, cancellationToken)
            .ConfigureAwait(false);
        if (alwaysListened.Contains(chatId))
            return true;

        var listeningMode = await kvas.ChatUserSettings(chatId)
            .Get(x => x.ListeningMode, cancellationToken)
            .ConfigureAwait(false);
        return listeningMode == ListeningMode.Forever;
    }

    private static (ChatId? ChatId, long EntryLid) GetReadAnchor(Notification notification)
        => notification switch {
            ConversationNotification n => (n.ChatId, n.EndEntryLid),
            ChatEntryRelatedNotification n => (n.ChatId, n.EntryLid),
            ChatEntryNotification n => (n.ChatId, n.EntryLid),
            _ => (null, 0L),
        };

    private async ValueTask EnqueueMessageRelatedNotifications(
        ChatId chatId,
        ChatEntryId? entryId,
        AuthorFull changeAuthor,
        string content,
        NotificationKind kind,
        string similarityKey,
        IReadOnlyList<UserId> userIds,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug("-> EnqueueMessageRelatedNotifications. ChatId={ChatId}, EntryId={EntryId}, "
            + "Kind={Kind}, UserIds#={UserIdCount}",
            chatId, entryId, kind, userIds.Count);

        if (entryId is not null && entryId.ChatId != chatId)
            throw new ArgumentOutOfRangeException(nameof(entryId), "entry.ChatId should match given chatId");

        var chat = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        var title = NotificationHelper.GetTitle(chat, changeAuthor);
        var iconUrl = NotificationHelper.GetIconUrl(chat, changeAuthor, UrlMapper);
        var now = Clocks.CoarseSystemClock.Now;
        var otherUserIds = userIds.Where(userId => userId != changeAuthor.UserId);

        foreach (var otherUserId in otherUserIds) {
            var entryLid = entryId?.LocalId ?? 0;
            var fullEntryId = entryId ?? ChatEntryId.New(chatId, entryLid);
            Notification notification = kind switch {
                NotificationKind.Message => MessageNotification.New(otherUserId, chatId, entryLid, changeAuthor.Id),
                NotificationKind.Reply => ReplyNotification.New(otherUserId, chatId, entryLid, changeAuthor.Id),
                NotificationKind.Thread => ThreadNotification.New(otherUserId, chatId, entryLid, changeAuthor.Id),
                NotificationKind.Invitation => InvitationNotification.New(otherUserId, chatId, changeAuthor.Id),
                NotificationKind.Mention => MentionNotification.New(otherUserId, fullEntryId, changeAuthor.Id),
                NotificationKind.Reaction => ReactionNotification.New(otherUserId, fullEntryId, changeAuthor.Id),
                NotificationKind.Attention => AttentionNotification.New(otherUserId, fullEntryId, changeAuthor.Id),
                _ => throw StandardError.NotSupported<NotificationsBackend>($"Unsupported notification kind: {kind}."),
            };
            notification = notification with {
                Title = title,
                Text = content,
                IconUrl = iconUrl,
                SentAt = now,
            };
            if (notification is ChatEntryRelatedNotification related)
                notification = related with {
                    StartEntryLid = entryLid,
                    UnreadCount = 1,
                    AuthorIds = new[] { changeAuthor.Id }.ToApiArray(),
                    RecentMessages = new[] {
                        NotificationMessage.New(changeAuthor.Id, changeAuthor.Avatar.Name, content, entryLid, now),
                    }.ToApiArray(),
                    LeadText = content,
                    LeadCount = 1,
                };
            await Queues.Enqueue(new NotificationsBackend_Notify(notification), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<Device>> ListDevices(
        UserId userId, Symbol sessionHash, Moment? minActiveAt, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        // AccessedAt is null until the same token re-registers, so freshness falls back to CreatedAt.
        var minActiveAtValue = minActiveAt?.ToDateTime() ?? default;
        var dbDevices = await dbContext.Devices
            .Where(d => d.UserId == userId.Value)
            .WhereIf(d => d.SessionHash == sessionHash.Value, !sessionHash.IsEmpty)
            .WhereIf(d => (d.AccessedAt ?? d.CreatedAt) >= minActiveAtValue, minActiveAt.HasValue)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var devices = dbDevices.Select(d => d.ToModel()).ToList();
        return devices;
    }

    private async Task NotifyMembersInternal(
        UserId userId, ChatId chatId, long textEntryLid, IReadOnlyList<UserId> userIds,
        CancellationToken cancellationToken)
    {
        var author = await AuthorsBackend
            .GetByUserId(chatId, userId, RequestedAuthorKind.Full, cancellationToken)
            .Require()
            .ConfigureAwait(false);

        var now = Clocks.CoarseSystemClock.Now;
        var similarityKey = now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var content = $"{author.Avatar.Name} asks for attention";
        var lastEntryId = ChatEntryId.New(chatId, textEntryLid);
        await EnqueueMessageRelatedNotifications(
            chatId, lastEntryId, author, content, NotificationKind.Attention,
            similarityKey, userIds, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<UserId[]> FilterByNotificationMode(
        IReadOnlyList<UserId> userIds,
        ChatId chatId,
        NotificationImportance importance,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return [];
        if (importance == NotificationImportance.Ringer)
            return userIds.ToArray();

        var notificationModes = await userIds
            .Select(async userId => {
                var kvas = ServerKvasBackend.ForUser(userId);
                var notificationMode = await kvas.ChatUserSettings(chatId)
                    .Get(x => x.NotificationMode, cancellationToken)
                    .ConfigureAwait(false);
                return (UserId: userId, NotificationMode: notificationMode);
            })
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        var subscriberIds = notificationModes
            .Where(kv => NotificationHelper.IsDeliverable(importance, kv.NotificationMode))
            .Select(kv => kv.UserId)
            .ToArray();
        return subscriberIds;
    }

    private async Task<UserId[]> FilterByFollowThreadStatus(IReadOnlyList<UserId> userIds, ChatId chatId,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
            return Array.Empty<UserId>();

        var subscriberIdWithStatus = await userIds
            .Select<UserId, Task<(UserId, bool)>>(async subscriberId  => {
                var contactId = ContactId.NewAny(subscriberId, chatId);
                var threadContact = await ContactsBackend
                    .GetThreadContact(subscriberId, contactId, cancellationToken)
                    .ConfigureAwait(false);
                var isFollowingThread = threadContact is not null;
                return (subscriberId, isFollowingThread);
            })
            .Collect(ApiConstants.Concurrency.Low, cancellationToken)
            .ConfigureAwait(false);
        return subscriberIdWithStatus
            .Where(c => c.Item2)
            .Select(c => c.Item1)
            .ToArray();
    }

    private static ChatEntryId? GetEntryId(Notification notification)
        => notification switch {
            ConversationNotification n => ChatEntryId.New(n.ChatId, n.StartEntryLid),
            ChatEntryRelatedNotification n => n.StartEntryId,
            ChatEntryNotification n => n.EntryId,
            _ => null,
        };

    private static bool IsSoftUpdate(UserNotificationInfo info, Notification notification)
    {
        var hasSimilar = info.Displayed.Any(n => n.Id == notification.Id);
        if (!hasSimilar)
            return false; // First notification for this key -> hard update

        if (notification is ChatEntryNotification)
            return false; // Individually-seen (mention / reaction / attention) -> hard update

        var sinceLastPush = notification.SentAt - info.LastPushAt;
        return sinceLastPush <= Constants.Notification.SilencePeriod;
    }

    private void EnqueueSoft(
        UserId userId, Notification notification, UserNotificationInfo info, TimeSpan minDelay = default)
    {
        // minDelay floors the coalescing delay: the active-reader grace must elapse even when the
        // previous push is old (which would otherwise process the buffer immediately).
        var now = Clocks.SystemClock.Now;
        var delay = Constants.Notification.SilencePeriod - (now - info.LastPushAt);
        if (delay < minDelay)
            delay = minDelay;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        var processAt = now + delay;

        bool mustSchedule;
        while (true) {
            var buffer = _softBuffers.GetOrAdd(userId, static _ => new SoftBuffer());
            lock (buffer.Lock) {
                if (buffer.IsRemoved)
                    continue; // a concurrent drain evicted this instance -> get/create a fresh one
                buffer.Pending.Add(notification);
                if (processAt > buffer.ProcessAt)
                    buffer.ProcessAt = processAt; // raise the floor so a grace request isn't truncated
                mustSchedule = !buffer.IsProcessScheduled;
                buffer.IsProcessScheduled = true;
            }
            break;
        }
        if (mustSchedule)
            ScheduleProcess(userId);
    }

    private void ScheduleProcess(UserId userId)
        => _ = Task.Run(async () => {
            try {
                // Wait until the buffer's ProcessAt floor is reached. A later EnqueueSoft can raise it
                // (an active-reader grace on top of a sooner coalescing tick), so re-read after each wait.
                while (_softBuffers.TryGetValue(userId, out var buffer)) {
                    TimeSpan remaining;
                    lock (buffer.Lock)
                        remaining = buffer.ProcessAt - Clocks.SystemClock.Now;
                    if (remaining <= TimeSpan.Zero)
                        break;
                    await Task.Delay(remaining, StopToken).ConfigureAwait(false);
                }
                // Outermost: this fires long after the originating command's context (and its scoped
                // service provider) is disposed, so it must not nest under the ambient CommandContext.
                await Commander.Call(new NotificationsBackend_Process(userId), true, StopToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                // Host is shutting down; the soft buffer is in-memory and intentionally transient.
            }
            catch (Exception e) {
                Log.LogError(e, "Deferred notification processing failed for UserId={UserId}", userId);
            }
        }, StopToken);

    private List<Notification> DrainSoftBuffer(UserId userId)
    {
        if (!_softBuffers.TryGetValue(userId, out var buffer))
            return [];

        lock (buffer.Lock) {
            buffer.IsProcessScheduled = false;
            var batch = new List<Notification>(buffer.Pending);
            buffer.Pending.Clear();
            // Evict the now-drained buffer so the map stays bounded by users with in-flight
            // soft updates. IsRemoved makes a racing EnqueueSoft retry with a fresh instance.
            buffer.IsRemoved = true;
            _softBuffers.TryRemove(new KeyValuePair<UserId, SoftBuffer>(userId, buffer));
            return batch;
        }
    }

    // The single DB-write + push path. Adds notifications, drops notifications whose entry
    // has been read or that were explicitly handled, then pushes the resulting delta.
    private async Task ApplyHardUpdate(
        UserId userId,
        IReadOnlyList<Notification> notifications,
        IReadOnlyCollection<NotificationId> handledIds,
        CancellationToken cancellationToken)
        => await ApplyHardUpdate(userId, notifications, handledIds, handleAll: false, cancellationToken)
            .ConfigureAwait(false);

    private async Task ApplyHardUpdate(
        UserId userId,
        IReadOnlyList<Notification> notifications,
        IReadOnlyCollection<NotificationId> handledIds,
        bool handleAll,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        context.Operation.MustStore(false);

        var dbUserNotifications = await dbContext.UserNotifications.ForUpdate()
            .FirstOrDefaultAsync(x => x.Id == userId.Value, cancellationToken)
            .ConfigureAwait(false);

        var (info, dismissed, silentById, reAnchored) = await Reconcile(
            dbUserNotifications?.ToModel() ?? new UserNotificationInfo(userId)).ConfigureAwait(false);
        if (silentById.Count == 0 && dismissed.Count == 0 && reAnchored.Count == 0)
            return; // Nothing changed (e.g. an already-dismissed OnHandle, or a redelivered batch)

        if (dbUserNotifications != null)
            dbUserNotifications.UpdateFrom(info);
        else {
            dbUserNotifications = new DbUserNotifications();
            dbUserNotifications.UpdateFrom(info);
            dbContext.UserNotifications.Add(dbUserNotifications);
        }

        try {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException e) when (e.Entries.All(en => en.State == EntityState.Added)) {
            // Lost the create race (INSERT ... ON CONFLICT DO NOTHING affected 0 rows):
            // the row exists now -> re-read it under lock and apply as an update instead.
            dbContext.Entry(dbUserNotifications).State = EntityState.Detached;
            dbUserNotifications = await dbContext.UserNotifications.ForUpdate()
                .FirstAsync(x => x.Id == userId.Value, cancellationToken)
                .ConfigureAwait(false);
            (info, dismissed, silentById, reAnchored) = await Reconcile(dbUserNotifications.ToModel())
                .ConfigureAwait(false);
            dbUserNotifications.UpdateFrom(info);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        context.Operation.AddCompletionHandler(scope => {
            using (Invalidation.Begin())
                _ = GetUserNotificationInfo(userId, default);
            return Task.CompletedTask;
        });

        // Push one banner per distinct client tag (per chat for coalescing kinds, per entry for
        // mentions/attention/reactions): a coalesced batch can add notifications for several
        // banners, and a single push would silently drop the others' while the badge still
        // counts them.
        // Pushes go out as operation events (the transactional outbox): they're persisted to
        // _events in this same commit and DbEventForwarder hands them to the queue, so a push
        // is never lost if this node dies after the commit. The badge is NOT snapshotted here —
        // OnPush/OnPushDismissal recompute it from current state at delivery, so out-of-order or
        // redelivered queue messages can't stamp a stale count.
        var toPush = notifications
            .Where(n => silentById.ContainsKey(n.Id))
            .GroupBy(GetPushGroupKey)
            .Select(g => g.MaxBy(n => n.SentAt)!)
            .ToList();
        foreach (var notification in toPush)
            context.Operation.AddEvent(
                new NotificationsBackend_Push(notification, silentById.GetValueOrDefault(notification.Id)));

        // Partial-read re-anchors update the banner content silently (a reduction, never an alert).
        // Tags already pushed above are skipped — OnPush sends the converged (re-anchored) state,
        // so a second push for the same banner would only make it re-post twice.
        var pushedTags = toPush.Select(GetPushGroupKey).ToHashSet();
        foreach (var notification in reAnchored.Where(n => !pushedTags.Contains(GetPushGroupKey(n))))
            context.Operation.AddEvent(new NotificationsBackend_Push(notification, IsSilent: true));

        // A newly displayed mention (re)starts the per-user reminder flow, which re-alerts unread
        // mentions until they're read. It's keyed by user, so repeated starts just resume it.
        var hasNewMention = notifications.Any(n =>
            n.Kind == NotificationKind.Mention && info.Displayed.Any(d => d.Id == n.Id));
        if (hasNewMention)
            context.Operation.AddEvent(FlowHub.NewResumeEvent<Flows.MentionReminderFlow>(userId.Value));
        if (dismissed.Count > 0) {
            // Only close banners whose tag is now fully gone — the chat banner may still be
            // backed by another active notification under the same tag. The badge is still
            // refreshed by the dismissal push regardless.
            var survivingTags = info.Displayed.Select(GetPushGroupKey).ToHashSet(StringComparer.Ordinal);
            var bannersToClose = dismissed.Where(d => !survivingTags.Contains(GetPushGroupKey(d))).ToApiArray();
            context.Operation.AddEvent(new NotificationsBackend_PushDismissal(userId, bannersToClose));
        }
        return;

        async Task<(
            UserNotificationInfo Info,
            IReadOnlyList<Notification> Dismissed,
            Dictionary<NotificationId, bool> SilentById,
            List<Notification> ReAnchored)> Reconcile(UserNotificationInfo committed)
        {
            var now = Clocks.SystemClock.Now;
            var readPositions = await GetReadPositions(
                    userId, committed.Displayed.Concat(notifications), cancellationToken)
                .ConfigureAwait(false);
            var current = committed;
            var dismissed = new List<Notification>();
            foreach (var existing in committed.Displayed) {
                var isGone = handleAll || handledIds.Contains(existing.Id) || IsRead(existing, readPositions);
                if (isGone) {
                    current = current with { Displayed = current.Displayed.Without(x => x.Id == existing.Id) };
                    dismissed.Add(existing);
                }
            }

            // Track which incoming notifications actually changed the displayed set: a no-op merge
            // (a redelivered duplicate, or a stale out-of-order event for an already-read entry)
            // must neither beep nor push. MergeWith returns the existing instance for no-op merges,
            // so reference equality detects them.
            var changedIds = new List<NotificationId>();
            foreach (var notification in notifications) {
                if (IsRead(notification, readPositions))
                    continue;

                var before = current.Displayed.FirstOrDefault(n => n.Id == notification.Id);
                current = current.WithNotification(notification);
                var after = current.Displayed.First(n => n.Id == notification.Id);
                if (!ReferenceEquals(before, after) && !changedIds.Contains(notification.Id))
                    changedIds.Add(notification.Id);
            }

            // For each notification the batch changed, decide whether its push audibly alerts or
            // updates silently. Coalescing chat notifications follow the beep back-off (composing
            // their summary text along the way); other kinds alert when first shown and update
            // silently on same-tag replacement — except ringers, which always alert.
            var silentById = new Dictionary<NotificationId, bool>();
            foreach (var id in changedIds) {
                var displayed = current.Displayed.First(n => n.Id == id);
                if (displayed is not ChatEntryRelatedNotification related) {
                    var isRinger = displayed.Kind is NotificationKind.Attention or NotificationKind.IncomingCall;
                    silentById[id] = !isRinger && committed.Displayed.Any(n => n.Id == id);
                    continue;
                }

                var shouldBeep = NotificationBeepPolicy.ShouldBeep(
                    related.Kind, related.BeepCount, related.LastBeepAt, now);
                var text = NotificationHelper.ComposeAggregatedText(related);
                var updated = related with {
                    Text = text,
                    BeepCount = shouldBeep ? related.BeepCount + 1 : related.BeepCount,
                    LastBeepAt = shouldBeep ? now : related.LastBeepAt,
                };
                current = current with { Displayed = current.Displayed.WithUpdate(n => n.Id == id, _ => updated) };
                silentById[id] = !shouldBeep;
            }

            // Partial read: the user read into (but not past) a coalesced notification's window.
            // Re-anchor it to the new first-unread entry and refresh its lead/count so the quote
            // tracks where they stopped instead of citing already-read messages.
            var reAnchored = new List<Notification>();
            foreach (var existing in current.Displayed) {
                if (existing is not ChatEntryRelatedNotification related)
                    continue;
                if (!readPositions.TryGetValue(related.ChatId, out var read) || read is <= 0 or long.MaxValue)
                    continue;
                var start = related.StartEntryLid > 0 ? related.StartEntryLid : related.EntryLid;
                if (read < start || read >= related.EntryLid)
                    continue; // nothing newly read here (fully-read ones were already dropped above)

                var reanchored = await ReAnchor(related, read + 1, cancellationToken).ConfigureAwait(false);
                current = current with {
                    Displayed = current.Displayed.WithUpdate(n => n.Id == related.Id, _ => reanchored),
                };
                reAnchored.Add(reanchored);
            }

            current = current with {
                Version = VersionGenerator.NextVersion(current.Version),
                LastPushAt = silentById.Count > 0 ? now : current.LastPushAt,
                IsDormant = current.Displayed.Count >= Constants.Notification.DormancyThreshold,
            };
            return (current, dismissed, silentById, reAnchored);
        }
    }

    // Partial read: drops now-read messages via ReAnchorAt; when every shown message was read,
    // re-seeds the window from the first still-unread entry so the banner isn't left textless.
    private async Task<ChatEntryRelatedNotification> ReAnchor(
        ChatEntryRelatedNotification related, long newStart, CancellationToken cancellationToken)
    {
        var updated = related.ReAnchorAt(newStart);
        if (updated.RecentMessages.IsEmpty) {
            var entry = await ChatsBackend
                .GetEntry(ChatEntryId.New(related.ChatId, newStart), TimeSpan.Zero, cancellationToken)
                .ConfigureAwait(false);
            if (entry is { IsSystemEntry: false }) {
                var (text, _) = await NotificationHelper
                    .GetText(entry, MarkupConsumer.Notification, ChatMarkupHubFactory, cancellationToken)
                    .ConfigureAwait(false);
                var author = await AuthorsBackend
                    .Get(related.ChatId, entry.AuthorId, RequestedAuthorKind.Full, cancellationToken)
                    .ConfigureAwait(false);
                var message = NotificationMessage.New(
                    entry.AuthorId, author?.Avatar.Name ?? "", text, entry.LocalId, entry.BeginsAt);
                updated = updated with {
                    RecentMessages = new[] { message }.ToApiArray(),
                    LeadText = message.Text,
                };
            }
        }
        return updated with { Text = NotificationHelper.ComposeAggregatedText(updated) };
    }

    // Banner grouping key = the client push tag. Uses the shared NotificationExt.GetPushTag so
    // the server's grouping and the client reconciler's matching can't drift; non-chat
    // notifications fall back to the dedup key.
    private static string GetPushGroupKey(Notification notification)
        => notification.GetPushTag() ?? notification.SimilarityKey;

    private sealed class SoftBuffer
    {
        public readonly Lock Lock = new();
        public readonly List<Notification> Pending = [];
        public bool IsProcessScheduled;
        public bool IsRemoved;
        // The floor time the pending batch must not drain before; raised (never lowered) as later
        // enqueues arrive, so an active-reader grace extends an already-scheduled sooner tick.
        public Moment ProcessAt;
    }
}
