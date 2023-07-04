using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.Contacts;

#pragma warning disable MA0049

public class Contacts : IContacts
{
    private IAccounts Accounts { get; }
    private IChats Chats { get; }
    private IContactsBackend Backend { get; }
    private ICommander Commander { get; }

    public Contacts(IServiceProvider services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        Chats = services.GetRequiredService<IChats>();
        Backend = services.GetRequiredService<IContactsBackend>();
        Commander = services.Commander();
    }

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
    public virtual async Task<ApiArray<ContactId>> ListIds(
        Session session,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var contactIds = await Backend.ListIds(account.Id, cancellationToken).ConfigureAwait(false);
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

    // Private methods

    private static Exception Unauthorized()
        => StandardError.Unauthorized("You can access only your own contacts.");
}
