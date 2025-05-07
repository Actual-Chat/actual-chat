namespace ActualChat.Contacts;

public static class ContactsBackendExt
{
    public static Task<ContactId[]> ListPeerContactIds(
        this IContactsBackend contactsBackend,
        UserId userId,
        CancellationToken cancellationToken)
        => contactsBackend.ListPeerContactIds(userId, null, cancellationToken);
}
