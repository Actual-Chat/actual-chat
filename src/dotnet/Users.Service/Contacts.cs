namespace ActualChat.Users;

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
    public virtual async Task<Contact?> GetOwn(Session session, string contactId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).Require().ConfigureAwait(false);
        var contact = await Backend.Get(contactId, cancellationToken).ConfigureAwait(false);
        if (contact?.OwnerUserId == account.Id)
            return contact;
        throw StandardError.Unauthorized("Contact is missing or you don't have access to it.");
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Contact>> ListOwn(
        Session session,
        CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return ImmutableArray<Contact>.Empty;

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

    // [CommandHandler]
    public virtual async Task<Contact> Change(IContacts.ChangeCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, id, expectedVersion, change) = command;
        change.RequireValid();

        var account = await Accounts.GetOwn(session, cancellationToken).Require().ConfigureAwait(false);
        if (!change.Create.HasValue) {
            // Update or Remove
            var contact = await Backend.Get(id, cancellationToken).Require().ConfigureAwait(false);
            if (contact.OwnerUserId != account.Id)
                throw StandardError.Unauthorized("Users can change only their own contacts.");
        }

        return await Commander
            .Call(new IContactsBackend.ChangeCommand(id, expectedVersion, change), cancellationToken)
            .ConfigureAwait(false);
    }
}
