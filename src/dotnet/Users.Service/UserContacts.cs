namespace ActualChat.Users;

public class UserContacts : IUserContacts
{
    private IAccounts Accounts { get; }
    private IUserContactsBackend Backend { get; }
    private ICommander Commander { get; }

    public UserContacts(IServiceProvider services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        Backend = services.GetRequiredService<IUserContactsBackend>();
        Commander = services.Commander();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<UserContact>> List(
        Session session,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.Get(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return ImmutableArray<UserContact>.Empty;

        var contactIds = await Backend.GetContactIds(account.Id, cancellationToken).ConfigureAwait(false);
        var contacts = await contactIds
            .Select(contactId => Backend.Get(contactId, cancellationToken))
            .Collect()
            .ConfigureAwait(false);
        return contacts
            .SkipNullItems()
            // .OrderBy(c => c.Name)
            .ToImmutableArray();
    }

    // [ComputeMethod]
    public virtual async Task<UserContact?> Get(Session session, string contactId, CancellationToken cancellationToken)
    {
        var account = await Accounts.Get(session, cancellationToken).Require().ConfigureAwait(false);
        var contact = await Backend.Get(contactId, cancellationToken).ConfigureAwait(false);
        if (contact?.OwnerUserId == account.Id)
            return contact;
        throw StandardError.Unauthorized("Contact is missing or you don't have access to it.");
    }

    // [CommandHandler]
    public virtual async Task<UserContact?> Change(IUserContacts.ChangeCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        // var (session, id, ownerUserId, targetUserId, expectedVersion, change) = command;
        var (session, id, expectedVersion, change) = command;
        var account = await Accounts.Get(session, cancellationToken).Require().ConfigureAwait(false);
        var contact = await Backend.Get(id, cancellationToken).Require().ConfigureAwait(false);
        if (contact.OwnerUserId != account.Id)
            throw StandardError.Unauthorized("Users can change only their own contacts.");

        return await Commander
            .Call(new IUserContactsBackend.ChangeCommand(id, expectedVersion, change), cancellationToken)
            .ConfigureAwait(false);
    }
}
