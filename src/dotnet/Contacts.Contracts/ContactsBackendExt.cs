namespace ActualChat.Contacts;

/// <summary>
/// Extension methods for <see cref="IContactsBackend"/>.
/// </summary>
public static class ContactsBackendExt
{
    public static Task<ContactId[]> ListPeerContactIds(
        this IContactsBackend contactsBackend,
        UserId userId,
        CancellationToken cancellationToken)
        => contactsBackend.ListPeerContactIds(userId, null, cancellationToken);
}
