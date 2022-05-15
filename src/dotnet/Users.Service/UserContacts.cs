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

    public virtual async Task<ImmutableArray<UserContact>> GetAll(Session session, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken).ConfigureAwait(false);
        if (!user.IsAuthenticated)
            return ImmutableArray<UserContact>.Empty;

        var contactIds = await _contactsBackend.GetContactIds(user.Id, cancellationToken).ConfigureAwait(false);
        var contacts = await Task.WhenAll(
            contactIds
                .Select(c => _contactsBackend.Get(c, cancellationToken))
                .ToArray()
            ).ConfigureAwait(false);
        return contacts.Where(c => c != null).Select(c => c!).ToImmutableArray();
    }
}
