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
    public virtual async Task<Contact?> Get(Session session, string id, CancellationToken cancellationToken)
    {
        var contactId = new ContactId(id);
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null || account.Id != contactId.OwnerId)
            return null;

        var contact = await Backend.Get(account.Id, id, cancellationToken).ConfigureAwait(false);
        if (contact == null)
            return null;

        var canRead = await Chats.HasPermissions(session, contact.ChatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return canRead ? contact : null;
    }

    // [ComputeMethod]
    public virtual async Task<Contact?> GetForChat(Session session, string chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).Require().ConfigureAwait(false);
        var ownerId = account.Id;

        var parsedChatId = new ChatId(chatId);
        ContactId id;
        if (parsedChatId.IsPeerChatId(ownerId, out var userId))
            id = new ContactId(ownerId, userId, SkipValidation.Instance);
        else if (parsedChatId.IsGroupChatId())
            id = new ContactId(ownerId, parsedChatId, SkipValidation.Instance);
        else
            return null;

        var contact = await Backend.Get(ownerId, id, cancellationToken).ConfigureAwait(false);
        if (contact == null)
            return null;

        var canRead = await Chats.HasPermissions(session, contact.ChatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return canRead ? contact : null;
    }

    // [ComputeMethod]
    public virtual async Task<Contact?> GetForUser(Session session, string userId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).Require().ConfigureAwait(false);
        var ownerId = account.Id;

        var id = new ContactId(ownerId, new UserId(userId), SkipValidation.Instance);
        var contact = await Backend.Get(ownerId, id, cancellationToken).ConfigureAwait(false);
        if (contact == null)
            return null;

        var canRead = await Chats.HasPermissions(session, contact.ChatId, ChatPermissions.Read, cancellationToken).ConfigureAwait(false);
        return canRead ? contact : null;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ContactId>> ListIds(
        Session session,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return ImmutableArray<ContactId>.Empty;

        var contactIds = await Backend.ListIds(account.Id, cancellationToken).ConfigureAwait(false);
        return contactIds;
    }

    // [CommandHandler]
    public virtual async Task<Contact> Change(IContacts.ChangeCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, id, expectedVersion, change) = command;
        id.RequireNonEmpty();
        change.RequireValid();

        var account = await Accounts.GetOwn(session, cancellationToken).Require().ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw StandardError.Unauthorized("Users can change only their own contacts.");

        return await Commander
            .Call(new IContactsBackend.ChangeCommand(id, expectedVersion, change), cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task Touch(IContacts.TouchCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, id) = command;
        id.RequireNonEmpty();

        var account = await Accounts.GetOwn(session, cancellationToken).Require().ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw StandardError.Unauthorized("Users can change only their own contacts.");

        await Commander
            .Call(new IContactsBackend.TouchCommand(id), cancellationToken)
            .ConfigureAwait(false);
    }
}
