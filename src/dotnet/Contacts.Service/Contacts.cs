using ActualChat.Users;

namespace ActualChat.Contacts;

#pragma warning disable MA0049

public class Contacts : IContacts
{
    private IAccounts Accounts { get; }
    private IContactsBackend Backend { get; }
    private ICommander Commander { get; }

    public Contacts(IServiceProvider services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        Backend = services.GetRequiredService<IContactsBackend>();
        Commander = services.Commander();
    }

    // [ComputeMethod]
    public virtual async Task<Contact?> Get(Session session, string id, CancellationToken cancellationToken)
    {
        var contactId = new ContactId(id);
        if (!contactId.IsFullyValid)
            return null;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null || account.Id != contactId.OwnerId)
            return null;

        var contact = await Backend.Get(account.Id, id, cancellationToken).ConfigureAwait(false);
        return contact;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ContactId>> ListIds(
        Session session,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return ImmutableArray<ContactId>.Empty;

        var contactIds = await Backend.List(account.Id, cancellationToken).ConfigureAwait(false);
        return contactIds;
    }

    // [CommandHandler]
    public virtual async Task<Contact> Change(IContacts.ChangeCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, id, expectedVersion, change) = command;
        id.RequireFullyValid();
        change.RequireValid();

        var account = await Accounts.GetOwn(session, cancellationToken).Require().ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw StandardError.Unauthorized("Users can change only their own contacts.");

        return await Commander
            .Call(new IContactsBackend.ChangeCommand(id, expectedVersion, change), cancellationToken)
            .ConfigureAwait(false);
    }

    // [CommandHandler]
    public async Task Touch(IContacts.TouchCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, id) = command;
        id.RequireFullyValid();

        var account = await Accounts.GetOwn(session, cancellationToken).Require().ConfigureAwait(false);
        if (id.OwnerId != account.Id)
            throw StandardError.Unauthorized("Users can change only their own contacts.");

        await Commander
            .Call(new IContactsBackend.TouchCommand(id), cancellationToken)
            .ConfigureAwait(false);
    }
}
