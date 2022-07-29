namespace ActualChat.Users;

public class UserContacts : IUserContacts
{
    private readonly IAuth _auth;
    private readonly IUserContactsBackend _contactsBackend;

    public UserContacts(IAuth auth, IUserContactsBackend contactsBackend)
    {
        _auth = auth;
        _contactsBackend = contactsBackend;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<UserContact>> List(Session session, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return ImmutableArray<UserContact>.Empty;

        var contactIds = await _contactsBackend.GetContactIds(user.Id, cancellationToken).ConfigureAwait(false);
        var contacts = await contactIds.Select(c => _contactsBackend.Get(c, cancellationToken))
            .Collect()
            .ConfigureAwait(false);

        return contacts.Where(c => c != null).Select(c => c!).ToImmutableArray();
    }
}
