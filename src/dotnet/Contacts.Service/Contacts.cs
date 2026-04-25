using ActualChat.Users;

namespace ActualChat.Contacts;

/// <summary>
/// Frontend service for managing user contacts and memberships with session-based access control.
/// </summary>
#pragma warning disable MA0049

public class Contacts(IServiceProvider services) : IContacts
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IPlaces Places => field ??= services.GetRequiredService<IPlaces>(); // Lazy resolving to prevent cyclic dependency
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
    public virtual async Task<PlaceId[]> ListPlaceIds(
        Session session,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var contactIds = await Backend.ListPlaceIds(account.Id, cancellationToken).ConfigureAwait(false);
        return contactIds;
    }

    // [ComputeMethod]
    public virtual async Task<ContactId[]> ListBannedIds(
        Session session,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await Backend.ListBannedIds(account.Id, cancellationToken).ConfigureAwait(false);
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

    // [CommandHandler]
    public virtual async Task<Contact?> OnChange(Contacts_Change command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var (session, id, expectedVersion, change) = command;
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
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, id) = command;
        id.Require();

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw Unauthorized();

        var touchCommand = new ContactsBackend_Touch(id);
        await Commander.Call(touchCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnSetIsBanned(Contacts_SetIsBanned command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var (session, id, isBanned) = command;
        id.Require();
        if (id.ChatId is not PeerChatId)
            throw StandardError.Constraint("Only peer contacts can be banned.");

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw Unauthorized();

        var setIsBannedCommand = new ContactsBackend_SetIsBanned(id, isBanned);
        await Commander.Call(setIsBannedCommand, true, cancellationToken).ConfigureAwait(false);
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
            => contact.State == ContactState.Temporary
                ? contact with { State = ContactState.Regular }
                : contact;
    }

    private static Exception Unauthorized()
        => StandardError.Unauthorized("You can access only your own contacts.");
}
