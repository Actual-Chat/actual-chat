namespace ActualChat.Notifications;

/// <summary>
/// Frontend service for managing push notifications with session-based access control.
/// </summary>
public class NotificationsService(IServiceProvider services) : INotifications
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private INotificationsBackend Backend { get; } = services.GetRequiredService<INotificationsBackend>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IPlaces Places { get; } = services.GetRequiredService<IPlaces>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
        = services.KeyedFactory<IBackendChatMarkupHub, ChatId>();
    private ILogger Log { get; } = services.LogFor<NotificationsService>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<ApiArray<Notification>> ListActive(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var info = await Backend.GetUserNotificationInfo(account.Id, cancellationToken).ConfigureAwait(false);
        return info.Displayed;
    }

    // [ComputeMethod]
    public virtual async Task<bool> HasNotifiedMentionedMembers(
        Session session,
        ChatEntryId chatEntryId,
        CancellationToken cancellationToken)
    {
        var chatId = chatEntryId.ChatId;
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return false;

        var chatEntry = await Chats.GetEntry(session, chatEntryId, cancellationToken).ConfigureAwait(false);
        if (chatEntry is null)
            return false;

        var author = chat.Rules.Author.Require();
        if (chatEntry.AuthorId != author.Id)
            return false;

        var notificationId = GetExplicitNotificationIdForNotifyMentionedMembers(author.UserId, chatEntryId);
        var notification = await Backend.GetExplicit(notificationId, cancellationToken).ConfigureAwait(false);
        return notification is not null;
    }

    // [CommandHandler]
    public virtual async Task OnHandle(
        Notifications_Handle command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var notificationId = command.NotificationId;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (notificationId.UserId != account.Id)
            throw Unauthorized();

        await Commander.Run(new NotificationsBackend_Handle(notificationId), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnHandleAll(
        Notifications_HandleAll command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        await Commander.Run(new NotificationsBackend_HandleAll(account.Id), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRegisterDevice(
        Notifications_RegisterDevice command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var deviceId = command.DeviceId;
        var deviceType = command.DeviceType;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull()) {
            Log.LogWarning("Skipping RegisterDevice for guest or none user." +
                " DeviceId: '{DeviceId}', DeviceType: '{DeviceType}', SessionHash: '{SessionHash}', UserId: '{UserId}'" ,
                deviceId, deviceType, session.Hash, account.Id);
            return;
        }
        var registerDeviceCommand = new NotificationsBackend_RegisterDevice(account.Id, deviceId, deviceType, session.Hash);
        await Commander.Run(registerDeviceCommand, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDeregisterDevice(
        Notifications_DeregisterDevice command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var deviceId = command.DeviceId;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var existingDevices = await Backend.ListDevices(account.Id, cancellationToken).ConfigureAwait(false);
        if (existingDevices.All(d => d.DeviceId != deviceId)) {
            Log.LogWarning("OnDeregisterDevice: non-existing device");
            return;
        }
        var registerDeviceCommand = new NotificationsBackend_RemoveDevices([deviceId]);
        await Commander.Run(registerDeviceCommand, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnNotifyMembers(
        Notifications_NotifyMembers command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var chatId = command.ChatId;
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        var author = chat.Rules.Author.Require();
        var account = chat.Rules.Account.Require();
        chat.Rules.Require(ChatPermissions.Write);

        var isPublic = chat.IsPublic;
        if (isPublic && chatId is PlaceChatId placeChatId) {
            var place = await Places.Get(session, placeChatId.PlaceId, cancellationToken).ConfigureAwait(false);
            isPublic &= place.Require().IsPublic;
        }
        if (isPublic)
            throw StandardError.Constraint("Notify members is not allowed in public accessible chats.");

        if (chatId.Kind != ChatKind.Peer) {
            var authorIds = await Authors.ListAuthorIds(session, chatId, cancellationToken).ConfigureAwait(false);
            // Always disabled for middle and large groups.
            if (authorIds.Length > 10)
                throw StandardError.Unavailable("Alert everyone is unavailable in chats with more than 10 people.");
        }

        var entryId = ChatEntryId.New(author.ChatId, 0);
        var changeEntry = new ChatsBackend_ChangeEntry(entryId, null,
            Change.Create(new ChatEntryDiff {
                Kind = ChatEntryKind.NotifyMembers,
                AuthorId = GetWalleId(author.ChatId),
                TargetAuthorId = author.Id,
                TargetAuthorName = author.ToString(),
            }));

        var textEntry = await Commander.Call(changeEntry, true, cancellationToken).ConfigureAwait(false);

        var notifyCommand = new NotificationsBackend_NotifyMembers(account.Id, chatId, textEntry.LocalId - 1);
        await Commander.Run(notifyCommand, cancellationToken).ConfigureAwait(false);

        static AuthorId GetWalleId(ChatId chatId)
            => AuthorId.New(chatId, Constants.User.Walle.AuthorLocalId);
    }

    // [CommandHandler]
    public virtual async Task OnNotifyMentionedMembers(Notifications_NotifyMentionedMembers command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var session = command.Session;
        var ChatEntryId = command.ChatEntryId;
        var chatId = ChatEntryId.ChatId;
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.IsMember().Require();
        var chatEntry = await Chats.GetEntry(session, ChatEntryId, cancellationToken).Require().ConfigureAwait(false);
        var ownAuthor = chat.Rules.Author.Require();
        if (chatEntry.AuthorId != ownAuthor.Id)
            throw StandardError.Unauthorized("Only the author is allowed to notify mentioned principals.");

        var mentionIds = await GetMentionIds().ConfigureAwait(false);
        var ownUserId = ownAuthor.UserId;
        var mentionedUserIds = await GetMentionedUserIds(ownUserId).ConfigureAwait(false);
        if (mentionedUserIds.Length == 0)
            throw StandardError.Constraint("Nobody to notify.");

        var notifyCommand = new NotificationsBackend_NotifyMentionedMembers(ownUserId, ChatEntryId, mentionedUserIds);
        await Commander.Run(notifyCommand, cancellationToken).ConfigureAwait(false);

        var notificationId = GetExplicitNotificationIdForNotifyMentionedMembers(ownUserId, ChatEntryId);
        var notification = new ExplicitNotification(notificationId);
        var upsertNotificationCommand = new NotificationsBackend_UpsertExplicitNotification(notification);
        await Commander.Run(upsertNotificationCommand, cancellationToken).ConfigureAwait(false);
        return;

        async Task<HashSet<MentionRef>> GetMentionIds()
        {
            var chatMarkupHub = ChatMarkupHubFactory[chatEntry.ChatId];
            var markup = await chatMarkupHub.GetMarkup(chatEntry, MarkupConsumer.Notification, cancellationToken).ConfigureAwait(false);
            return MentionExtractor.Instance.GetMentionIds(markup);
        }

        async Task<UserId[]> GetMentionedUserIds(UserId excludeUserId)
        {
            var authorIds = mentionIds
                .Where(c => c.Target is AuthorId)
                .Select(c => (AuthorId)c.Target);
            var userIds = mentionIds
                .Where(c => c.Target is UserId)
                .Select(c => (UserId)c.Target);
            var authorsFromAuthorMentions = authorIds
                .Select(id => AuthorsBackend.Get(chatId, id, RequestedAuthorKind.Full, cancellationToken));
            var authorsFromUserMentions = userIds
                .Select(id => AuthorsBackend.GetByUserId(chatId, id, RequestedAuthorKind.Full, cancellationToken));
            var allAuthors = await authorsFromAuthorMentions
                .Concat(authorsFromUserMentions)
                .Collect(cancellationToken)
                .ConfigureAwait(false);
            return allAuthors
                .SkipNullItems()
                .Select(a => a.UserId)
                .Where(c => c != excludeUserId)
                .Distinct()
                .ToArray();
        }
    }

    // Private methods

    private static Exception Unauthorized()
        => StandardError.Unauthorized("You can access only your own notifications.");

    private static ExplicitNotificationId GetExplicitNotificationIdForNotifyMentionedMembers(UserId accountId, ChatEntryId chatEntryId)
        => ExplicitNotificationId.New(accountId, ExplicitNotificationKind.NotifyMentionedMembers, chatEntryId.Value);
}
