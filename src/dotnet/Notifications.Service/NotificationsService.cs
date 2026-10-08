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
        return info.Items;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<MentionAlertStatus>> ListMentionedMemberAlerts(
        Session session,
        ChatEntryId chatEntryId,
        CancellationToken cancellationToken)
    {
        var chatId = chatEntryId.ChatId;
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return default;

        var chatEntry = await Chats.GetEntry(session, chatEntryId, cancellationToken).ConfigureAwait(false);
        if (chatEntry is null)
            return default;

        var ownAuthor = chat.Rules.Author;
        if (ownAuthor is null || chatEntry.AuthorId != ownAuthor.Id)
            return default; // Only the message's own author sees per-mention alert state.

        var ownUserId = ownAuthor.UserId;
        var mentionIds = await GetMentionIds(chatEntry, cancellationToken).ConfigureAwait(false);
        var mentioned = await GetMentionedMembers(chatId, mentionIds, ownUserId, cancellationToken)
            .ConfigureAwait(false);
        var statuses = new List<MentionAlertStatus>(mentioned.Count);
        foreach (var (authorId, userId, mentionRef) in mentioned) {
            var notificationId = GetExplicitNotificationIdForNotifyMentionedMember(ownUserId, chatEntryId, authorId);
            var notification = await Backend.GetExplicit(notificationId, cancellationToken).ConfigureAwait(false);
            var hasRead = await Chats
                .IsEntryReadByMentionedUser(session, chatEntryId, mentionRef, cancellationToken)
                .ConfigureAwait(false);
            statuses.Add(new MentionAlertStatus(authorId, userId, notification is not null, hasRead == true));
        }
        return statuses.ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<long> GetHistoryVersion(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return 0;

        return await Backend.GetHistoryVersion(account.Id, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ApiArray<NotificationHistoryItem>> ListHistory(
        Session session, NotificationHistoryQuery query, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull())
            return ApiArray<NotificationHistoryItem>.Empty;

        return await Backend.ListHistory(account.Id, query, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDismiss(
        Notifications_Dismiss command, CancellationToken cancellationToken)
    {
        var session = command.Session;
        var notificationId = command.NotificationId;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (notificationId.UserId != account.Id)
            throw Unauthorized();

        await Commander.Run(new NotificationsBackend_Dismiss(notificationId), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDismissAll(
        Notifications_DismissAll command, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        await Commander.Run(new NotificationsBackend_DismissAll(account.Id), cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnRegisterDevice(
        Notifications_RegisterDevice command, CancellationToken cancellationToken)
    {
        var session = command.Session;
        var deviceId = command.DeviceId;
        var deviceType = command.DeviceType;
        var isPttEnabled = command.IsPttEnabled;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account.IsGuestOrNull()) {
            Log.LogWarning("Skipping RegisterDevice for guest or none user." +
                " DeviceId: '{DeviceId}', DeviceType: '{DeviceType}', SessionHash: '{SessionHash}', UserId: '{UserId}'" ,
                deviceId, deviceType, session.Hash, account.Id);
            return;
        }
        var registerDeviceCommand = new NotificationsBackend_RegisterDevice(
            account.Id, deviceId, deviceType, session.Hash, isPttEnabled);
        await Commander.Run(registerDeviceCommand, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDeregisterDevice(
        Notifications_DeregisterDevice command, CancellationToken cancellationToken)
    {
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
        var session = command.Session;
        var chatEntryId = command.ChatEntryId;
        var chatId = chatEntryId.ChatId;
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.IsMember().Require();
        var chatEntry = await Chats.GetEntry(session, chatEntryId, cancellationToken).Require().ConfigureAwait(false);
        var ownAuthor = chat.Rules.Author.Require();
        if (chatEntry.AuthorId != ownAuthor.Id)
            throw StandardError.Unauthorized("Only the author is allowed to notify mentioned principals.");

        var ownUserId = ownAuthor.UserId;
        var mentionIds = await GetMentionIds(chatEntry, cancellationToken).ConfigureAwait(false);
        var mentioned = await GetMentionedMembers(chatId, mentionIds, ownUserId, cancellationToken)
            .ConfigureAwait(false);
        // Empty AuthorIds = alert everyone mentioned; otherwise restrict to the requested subset
        if (command.AuthorIds.Count != 0) {
            var selected = command.AuthorIds.ToHashSet();
            mentioned = mentioned.Where(m => selected.Contains(m.AuthorId)).ToList();
        }
        if (mentioned.Count == 0)
            throw StandardError.Constraint("Nobody to notify.");

        var userIds = mentioned.Select(m => m.UserId).Distinct().ToArray();
        var notifyCommand = new NotificationsBackend_NotifyMentionedMembers(ownUserId, chatEntryId, userIds);
        await Commander.Run(notifyCommand, cancellationToken).ConfigureAwait(false);

        // One explicit notification per alerted author, so ListMentionedMemberAlerts can tell who's been alerted
        foreach (var (authorId, _, _) in mentioned) {
            var notificationId = GetExplicitNotificationIdForNotifyMentionedMember(ownUserId, chatEntryId, authorId);
            var notification = new ExplicitNotification(notificationId);
            var upsertCommand = new NotificationsBackend_UpsertExplicitNotification(notification);
            await Commander.Run(upsertCommand, cancellationToken).ConfigureAwait(false);
        }
    }

    // Private methods

    private static Exception Unauthorized()
        => StandardError.Unauthorized("You can access only your own notifications.");

    private async Task<HashSet<MentionRef>> GetMentionIds(ChatEntry chatEntry, CancellationToken cancellationToken)
    {
        var chatMarkupHub = ChatMarkupHubFactory[chatEntry.ChatId];
        var markup = await chatMarkupHub.GetMarkup(chatEntry, MarkupConsumer.Notification, cancellationToken)
            .ConfigureAwait(false);
        return MentionExtractor.Instance.GetMentionIds(markup);
    }

    // Keeps the original MentionRef per member: IsEntryReadByMentionedUser validates the ref is the one
    // actually in the markup, so a reconstructed author-ref would fail for a user-mention (@name u:...).
    private async Task<List<(AuthorId AuthorId, UserId UserId, MentionRef MentionRef)>> GetMentionedMembers(
        ChatId chatId,
        IEnumerable<MentionRef> mentionIds,
        UserId excludeUserId,
        CancellationToken cancellationToken)
    {
        var resolved = await mentionIds.Select(Resolve).Collect(cancellationToken).ConfigureAwait(false);
        return resolved
            .Where(x => x.Author is { } a && a.UserId != excludeUserId)
            .GroupBy(x => x.Author!.Id)
            .Select(g => g.First())
            .Select(x => (AuthorId: x.Author!.Id, UserId: x.Author!.UserId, MentionRef: x.MentionRef))
            .ToList();

        async Task<(MentionRef MentionRef, AuthorFull? Author)> Resolve(MentionRef mentionRef)
        {
            if (mentionRef.Target is AuthorId authorId)
                return (mentionRef, await AuthorsBackend
                    .Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false));
            if (mentionRef.Target is UserId userId)
                return (mentionRef, await AuthorsBackend
                    .GetByUserId(chatId, userId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false));

            return (mentionRef, null);
        }
    }

    private static ExplicitNotificationId GetExplicitNotificationIdForNotifyMentionedMember(
        UserId ownUserId, ChatEntryId chatEntryId, AuthorId authorId)
        => ExplicitNotificationId.New(
            ownUserId, ExplicitNotificationKind.NotifyMentionedMember, $"{chatEntryId.Value} {authorId.Value}");
}
