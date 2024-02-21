using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.Contacts;

#pragma warning disable MA0049

public class Contacts(IServiceProvider services) : IContacts
{
    private IPlaces? _places;

    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IContactsBackend Backend { get; } = services.GetRequiredService<IContactsBackend>();
    private ICommander Commander { get; } = services.Commander();

    private IPlaces Places => _places ??= services.GetRequiredService<IPlaces>(); // Lazy resolving to prevent cyclic dependency

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
        var contactId = new ContactId(ownAccount.Id, chatId, ParseOrNone.Option);
        if (contactId.IsNone)
            return null; // A peer chat that belongs to other users, etc.

        var contact = await Backend.Get(ownAccount.Id, contactId, cancellationToken).ConfigureAwait(false);
        var chat = await Chats.Get(session, contact.ChatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return null; // We don't return contacts w/ null Chat

        contact = contact with { Chat = chat };
        return contact;
    }

    // [ComputeMethod]
    public virtual Task<ApiArray<ContactId>> ListIds(
        Session session,
        CancellationToken cancellationToken)
        => ListIds(session, PlaceId.None, cancellationToken);

    // [ComputeMethod]
    public virtual async Task<ApiArray<PlaceId>> ListPlaceIds(
        Session session,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var contactIds = await Backend.ListPlaceIds(account.Id, cancellationToken).ConfigureAwait(false);
        return contactIds;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<ContactId>> ListIds(
        Session session,
        PlaceId placeId,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var accountId = account.Id;
        var contactIds = await Backend.ListIds(accountId, placeId, cancellationToken).ConfigureAwait(false);
        // Add peer contacts for place members
        if (!placeId.IsNone) {
            var peerContacts = await GetPeerContacts(accountId, cancellationToken).ConfigureAwait(false);
            var memberUserIds = await Places.ListUserIds(session, placeId, cancellationToken).ConfigureAwait(false);
            var memberContactIds = new ApiSet<ContactId>();
            foreach (var userId in memberUserIds)
                if (peerContacts.TryGetValue(userId, out var contactId))
                    memberContactIds.Add(contactId);
            if (memberContactIds.Count > 0)
                contactIds = contactIds.Concat(memberContactIds).ToApiArray();
        }
        return contactIds;
    }

    // [CommandHandler]
    public virtual async Task<Contact?> OnChange(Contacts_Change command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, id, expectedVersion, change) = command;
        id.Require();
        change.RequireValid();

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw Unauthorized();

        return await Commander
            .Call(new ContactsBackend_Change(id, expectedVersion, change), cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnTouch(Contacts_Touch command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, id) = command;
        id.Require();

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw Unauthorized();

        await Commander
            .Call(new ContactsBackend_Touch(id), cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    [Obsolete("2023.10: No not available for clients anymore.")]
    public virtual Task OnGreet(Contacts_Greet command, CancellationToken cancellationToken)
        => Task.CompletedTask;

    // Protected methods

    [ComputeMethod]
    protected virtual async Task<Dictionary<UserId, ContactId>> GetPeerContacts(UserId accountId, CancellationToken cancellationToken)
    {
        var chatContactIds = await Backend.ListIds(accountId, PlaceId.None, cancellationToken).ConfigureAwait(false);
        return chatContactIds
            .Where(c => !c.ChatId.PeerChatId.IsNone)
            .Select(c => (Contact: c, UserId: c.ChatId.PeerChatId.UserIds.OtherThan(accountId)))
            .ToDictionary(c => c.UserId, c => c.Contact);
    }

    // Private methods

    private static Exception Unauthorized()
        => StandardError.Unauthorized("You can access only your own contacts.");
}
