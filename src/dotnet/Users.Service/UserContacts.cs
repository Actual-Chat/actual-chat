namespace ActualChat.Users;

public class UserContacts : IUserContacts
{
    private IAuth Auth { get; }
    private IUserContactsBackend ContactsBackend {get; }
    private ICommander Commander { get; }

    public UserContacts(IAuth auth, IUserContactsBackend contactsBackend, ICommander commander)
    {
        Auth = auth;
        ContactsBackend = contactsBackend;
        Commander = commander;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<UserContact>> List(
        Session session,
        CancellationToken cancellationToken)
    {
        var user = await Auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return ImmutableArray<UserContact>.Empty;

        var contactIds = await ContactsBackend.GetContactIds(user.Id, cancellationToken).ConfigureAwait(false);
        var contacts = await contactIds
            .Select(c => ContactsBackend.Get(c, cancellationToken))
            .Collect()
            .ConfigureAwait(false);
        return contacts
            .SkipNullItems()
            .OrderBy(c => c.Name)
            .ToImmutableArray();
    }

    // [CommandHandler]
    public virtual async Task<UserContact?> Change(IUserContacts.ChangeCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        // var (session, id, ownerUserId, targetUserId, expectedVersion, change) = command;
        var (session, id, expectedVersion, change) = command;
        var user = await Auth.GetUser(session, cancellationToken).Require().ConfigureAwait(false);
        var contact = await ContactsBackend.Get(id, cancellationToken).Require().ConfigureAwait(false);
        if (contact.OwnerUserId != user.Id)
            throw StandardError.Unauthorized("Users can change only their own contacts");

        return await Commander
            .Call(new IUserContactsBackend.ChangeCommand(id, expectedVersion, change), cancellationToken)
            .ConfigureAwait(false);
    }
}
