using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.Contacts;

#pragma warning disable MA0049

public class Contacts(IServiceProvider services) : IContacts
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    [field: AllowNull, MaybeNull]
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
    public virtual async Task<ContactId[]> ListIds(
        Session session,
        PlaceId? placeId,
        CancellationToken cancellationToken)
    {
        var isChatRoulette = placeId == Constants.Place.ChatRouletteId;
        if (placeId is not null && !isChatRoulette) {
            var place = await Places.Get(session, placeId, cancellationToken).ConfigureAwait(false);
            if (place?.Rules.CanRead() != true)
                return [];
        }

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var accountId = account.Id;
        var contactIds = await Backend.ListIds(accountId, placeId, cancellationToken).ConfigureAwait(false);
        // Add peer contacts for place members
        if (placeId is not null && !isChatRoulette) {
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
        var (session, id, expectedVersion, change) = command;
        id.Require();
        change.RequireValid();

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw Unauthorized();

        var changeCommand = new ContactsBackend_Change(id, expectedVersion, change);
        return await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnTouch(Contacts_Touch command, CancellationToken cancellationToken)
    {
        var (session, id) = command;
        id.Require();

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw Unauthorized();

        var touchCommand = new ContactsBackend_Touch(id);
        await Commander.Call(touchCommand, true, cancellationToken).ConfigureAwait(false);
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

    private static Exception Unauthorized()
        => StandardError.Unauthorized("You can access only your own contacts.");
}
