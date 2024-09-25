namespace ActualChat.Contacts;

public static class ContactsBackendExt
{
    public static Task<ApiArray<ContactId>> ListPeerContactIds(
        this IContactsBackend contactsBackend,
        UserId userId,
        CancellationToken cancellationToken)
        => contactsBackend.ListPeerContactIds(userId, PlaceId.None, cancellationToken);
}
