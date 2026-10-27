namespace ActualChat.Contacts;

/// <summary>
/// Frontend service for managing user contacts and memberships with session-based access control.
/// </summary>
#pragma warning disable MA0049

public class Contacts(IServiceProvider services) : IContacts
{
    private IServiceProvider Services { get; } = services;
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IPlaces Places => field ??= Services.GetRequiredService<IPlaces>();
    private IChatThreads ChatThreads => field ??= Services.GetRequiredService<IChatThreads>();
    private IChatPositions ChatPositions => field ??= Services.GetRequiredService<IChatPositions>();
    // Lazy: only API hosts register them, and IContacts is a local API on backend hosts too
    private IMentions Mentions => field ??= Services.GetRequiredService<IMentions>();
    private IUserSettings UserSettings => field ??= Services.GetRequiredService<IUserSettings>();
    private IContactsBackend Backend { get; } = services.GetRequiredService<IContactsBackend>();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<Contact?> Get(Session session, ContactId contactId, CancellationToken cancellationToken)
    {
        var ownAccount = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (ownAccount.Id != contactId.OwnerId)
            throw Unauthorized();

        var contact = await Backend.Get(ownAccount.Id, contactId, cancellationToken).ConfigureAwait(false);
        var chat = await Chats.Get(session, contact.ChatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null; // We don't return contacts w/ null Chat

        contact = contact with { Chat = chat };
        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<Contact?> GetForChat(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var ownAccount = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (chatId is PeerChatId peerChatId && !peerChatId.HasUser(ownAccount.Id))
            return null;

        var contactId = ContactId.NewAny(ownAccount.Id, chatId);
        var contact = await Backend.Get(ownAccount.Id, contactId, cancellationToken).ConfigureAwait(false);
        var chat = await Chats.Get(session, contact.ChatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null; // We don't return contacts w/ null Chat

        contact = contact with { Chat = chat };
        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<ContactSummary?> GetSummary(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var contact = await GetForChat(session, chatId, cancellationToken).ConfigureAwait(false);
        if (contact == null)
            return null;

        var lastOwnMentionTask = Mentions.GetLastOwn(session, chatId, cancellationToken);
        var chatUserSettingsKey = ChatUserSettings.GetKey(chatId);
        var chatUserSettingsTask = UserSettings.Get(session, chatUserSettingsKey, cancellationToken);
        var lastOwnMention = await lastOwnMentionTask.ConfigureAwait(false);
        var chatUserSettings = await chatUserSettingsTask.ConfigureAwait(false) as ChatUserSettings;
        return new ContactSummary(contact, lastOwnMention, chatUserSettings ?? ChatUserSettings.Default);
    }

    // [ComputeMethod]
    public virtual async Task<PlaceId[]> ListPlaceIds(
        Session session,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var contactIds = await Backend.ListPlaceIds(account.Id, cancellationToken).ConfigureAwait(false);
        return contactIds;
    }

    // [ComputeMethod]
    public virtual async Task<ContactId[]> ListBlockedIds(
        Session session,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await Backend.ListBlockedIds(account.Id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ContactId[]> ListIds(
        Session session,
        PlaceId? placeId,
        CancellationToken cancellationToken)
    {
        if (placeId is not null) {
            var place = await Places.Get(session, placeId, cancellationToken).ConfigureAwait(false);
            if (place?.Rules.CanRead() != true)
                return [];
        }

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var accountId = account.Id;
        var contactIds = await Backend.ListIds(accountId, placeId, cancellationToken).ConfigureAwait(false);
        // Add peer contacts for place members
        if (placeId is not null) {
            var peerContacts = await GetPeerContacts(accountId, cancellationToken).ConfigureAwait(false);
            var memberUserIds = await Places.ListUserIds(session, placeId, cancellationToken).ConfigureAwait(false);
            var memberContactIds = new ApiSet<ContactId>();
            foreach (var userId in memberUserIds)
                if (peerContacts.TryGetValue(userId, out var contactId))
                    memberContactIds.Add(contactId);
            if (memberContactIds.Count > 0)
                contactIds = contactIds.Concat(memberContactIds).ToArray();
        }
        return contactIds;
    }

    // [ComputeMethod]
    public virtual async Task<int> GetArchivedCount(
        Session session,
        PlaceId? placeId,
        CancellationToken cancellationToken)
    {
        var ownerId = await GetArchiveOwnerId(session, placeId, cancellationToken).ConfigureAwait(false);
        if (ownerId is null)
            return 0;

        return await Backend.GetArchivedCount(ownerId, placeId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ContactId[]> ListArchivedIds(
        Session session,
        PlaceId? placeId,
        Tile<int> indexTile,
        CancellationToken cancellationToken)
    {
        // Only the tiles of the layer: any other range would be a cache entry of its own per caller
        var tileLayer = Constants.Contacts.ArchiveIndexTiles;
        if (indexTile.Start < 0 || !tileLayer.GetTile(indexTile.Start).Range.Equals(indexTile.Range))
            throw StandardError.Constraint("Invalid archive tile.");

        var ownerId = await GetArchiveOwnerId(session, placeId, cancellationToken).ConfigureAwait(false);
        if (ownerId is null)
            return [];

        return await Backend.ListArchivedIds(ownerId, placeId, indexTile, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<UnreadChat>> ListUnreadChats(
        Session session,
        ContactSubset subset,
        CancellationToken cancellationToken)
    {
        if (subset.IsAll()) {
            var subsets = await ListAllSubsets(session, cancellationToken).ConfigureAwait(false);
            var lists = await subsets
                .Select(s => ListUnreadChats(session, s, cancellationToken))
                .Collect(ApiConstants.Concurrency.High, cancellationToken)
                .ConfigureAwait(false);
            return lists
                .SelectMany(x => x)
                .DistinctBy(x => x.ChatId)
                .OrderBy(x => x.ChatId.Value)
                .ToApiArray();
        }

        var ranges = await ListChatIdRanges(session, subset, cancellationToken).ConfigureAwait(false);
        var unreadChats = await ranges
            .Where(x => !x.IdRange.IsEmptyOrNegative)
            .Select(x => GetUnreadChat(session, x, cancellationToken))
            .Collect(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);
        return unreadChats.SkipNullItems().ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<ChatIdRange>> ListChatIdRanges(
        Session session,
        ContactSubset subset,
        CancellationToken cancellationToken)
    {
        if (subset.IsAll()) {
            // One cached list per place, so a message in one place doesn't recompute the others
            var subsets = await ListAllSubsets(session, cancellationToken).ConfigureAwait(false);
            var lists = await subsets
                .Select(s => ListChatIdRanges(session, s, cancellationToken))
                .Collect(ApiConstants.Concurrency.High, cancellationToken)
                .ConfigureAwait(false);
            return lists
                .SelectMany(x => x)
                .DistinctBy(x => x.ChatId)
                .OrderBy(x => x.ChatId.Value)
                .ToApiArray();
        }

        var placeId = subset.IsPlace(out var subsetPlaceId) ? subsetPlaceId : null;
        var contactIds = await ListIds(session, placeId, cancellationToken).ConfigureAwait(false);
        var threadIds = await ChatThreads.ListIdsForPlace(session, placeId, cancellationToken).ConfigureAwait(false);
        var chatIds = contactIds
            .Where(id => id.Kind is ContactKind.User or ContactKind.Chat)
            .Select(id => id.ChatId)
            .Concat(threadIds.Select(id => (ChatId)id))
            .Distinct();
        var ranges = await chatIds
            .Select(chatId => GetChatIdRange(session, chatId, cancellationToken))
            .Collect(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);
        return ranges.SkipNullItems().OrderBy(x => x.ChatId.Value).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<Contact?> OnChange(Contacts_Change command, CancellationToken cancellationToken)
    {
        var session = command.Session;
        var id = command.Id;
        var expectedVersion = command.ExpectedVersion;
        var change = command.Change;
        id.Require();
        change.RequireValid();

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw Unauthorized();

        change = EnsureStoredContactState(change);
        var changeCommand = new ContactsBackend_Change(id, expectedVersion, change);
        return await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnTouch(Contacts_Touch command, CancellationToken cancellationToken)
    {
        var session = command.Session;
        var id = command.Id;
        id.Require();

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw Unauthorized();

        var touchCommand = new ContactsBackend_Touch(id);
        await Commander.Call(touchCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnSetIsBlocked(Contacts_SetIsBlocked command, CancellationToken cancellationToken)
    {
        var session = command.Session;
        var id = command.Id;
        var isBlocked = command.IsBlocked;
        id.Require();
        if (id.ChatId is not PeerChatId)
            throw StandardError.Constraint("Only peer contacts can be blocked.");

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw Unauthorized();

        var setIsBlockedCommand = new ContactsBackend_SetIsBlocked(id, isBlocked);
        await Commander.Call(setIsBlockedCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnArchive(Contacts_Archive command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        id.Require();
        await RequireOwn(command.Session, id, cancellationToken).ConfigureAwait(false);

        var archiveCommand = new ContactsBackend_Archive(id);
        await Commander.Call(archiveCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnUnarchive(Contacts_Unarchive command, CancellationToken cancellationToken)
    {
        var id = command.Id;
        id.Require();
        await RequireOwn(command.Session, id, cancellationToken).ConfigureAwait(false);

        var unarchiveCommand = new ContactsBackend_Unarchive(id);
        await Commander.Call(unarchiveCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<Dictionary<UserId, ContactId>> GetPeerContacts(UserId accountId, CancellationToken cancellationToken)
    {
        var chatContactIds = await Backend.ListIds(accountId, null, cancellationToken).ConfigureAwait(false);
        return chatContactIds
            .Where(c => c.Kind == ContactKind.User)
            .Select(c => (Contact: c, UserId: ((PeerChatId)c.ChatId).UserIds.OtherThan(accountId)))
            .ToDictionary(c => c.UserId, c => c.Contact);
    }

    // Private methods

    private static Change<Contact> EnsureStoredContactState(Change<Contact> change)
    {
        if (change.IsCreate(out var create))
            return Change.Create(EnsureStored(create));
        if (change.IsUpdate(out var update))
            return Change.Update(EnsureStored(update));
        return change;

        static Contact EnsureStored(Contact contact)
        {
            // Blocking is owned exclusively by OnSetIsBlocked (which bypasses this path)
            // and is valid only for peer contacts. The generic change path normalizes any
            // other state to Regular; an existing peer block is preserved so that e.g.
            // renaming a blocked peer doesn't unblock it.
            var state = contact.State;
            if (state == ContactState.Temporary)
                state = ContactState.Regular;
            else if (state == ContactState.Blocked && contact.Kind != ContactKind.User)
                state = ContactState.Regular;
            return state == contact.State ? contact : contact with { State = state };
        }
    }

    private async Task<ContactSubset[]> ListAllSubsets(Session session, CancellationToken cancellationToken)
    {
        var placeIds = await ListPlaceIds(session, cancellationToken).ConfigureAwait(false);
        return [ContactSubset.Chats(), ..placeIds.Select(ContactSubset.Place)];
    }

    private async Task<UnreadChat?> GetUnreadChat(
        Session session, ChatIdRange chatIdRange, CancellationToken cancellationToken)
    {
        var (chatId, idRange) = chatIdRange;
        var position = await ChatPositions
            .GetOwn(session, chatId, ChatPositionKind.Read, cancellationToken)
            .ConfigureAwait(false);
        var lastEntryLid = chatIdRange.LastEntryLid;
        if (ChatPosition.GetUnreadCount(chatId, position.EntryLid, lastEntryLid) == 0)
            return null;

        var readEntryLid = Math.Max(position.EntryLid, idRange.Start - 1);
        return new UnreadChat(chatId, readEntryLid, lastEntryLid);
    }

    private async Task<ChatIdRange?> GetChatIdRange(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        try {
            var idRange = await Chats.GetIdRange(session, chatId, cancellationToken).ConfigureAwait(false);
            return new ChatIdRange(chatId, idRange);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            // A chat the user can't read any longer is left out rather than failing the whole list
            return null;
        }
    }

    private async Task<UserId?> GetArchiveOwnerId(
        Session session, PlaceId? placeId, CancellationToken cancellationToken)
    {
        if (placeId is not null) {
            var place = await Places.Get(session, placeId, cancellationToken).ConfigureAwait(false);
            if (place?.Rules.CanRead() != true)
                return null;
        }

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return account.Id;
    }

    private async Task RequireOwn(Session session, ContactId id, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw Unauthorized();
    }

    private static Exception Unauthorized()
        => StandardError.Unauthorized("You can access only your own contacts.");
}
