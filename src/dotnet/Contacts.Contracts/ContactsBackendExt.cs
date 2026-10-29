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

    public static async Task<string> GetAuthorName(
        this IContactsBackend contactsBackend,
        AuthorFull author,
        UserId readerId,
        CancellationToken cancellationToken)
    {
        // The reader's rename of the author wins over the author's own name wherever the reader sees it
        var name = author.Avatar.Name;
        if (author.IsAnonymous || author.UserId.IsGuestOrNull())
            return name; // Never de-anonymize
        if (readerId.IsGuestOrNull() || readerId == author.UserId)
            return name;

        var contactId = ContactId.NewUser(readerId, author.UserId);
        var contact = await contactsBackend.Get(readerId, contactId, cancellationToken).ConfigureAwait(false);
        return contact.PreferredPeerName ?? name;
    }

    public static async Task<Contact[]> ListRecent(
        this IContactsBackend contactsBackend,
        UserId userId,
        CancellationToken cancellationToken)
    {
        // TouchedAt moves when the owner posts in the chat, so this is "the chats I recently wrote to"
        var placeIds = await contactsBackend.ListPlaceIds(userId, cancellationToken).ConfigureAwait(false);
        var contactIds = new List<ContactId>();
        foreach (var placeId in placeIds.Select(x => (PlaceId?)x).Prepend(null)) {
            var placeContactIds = await contactsBackend
                .ListIds(userId, placeId, cancellationToken)
                .ConfigureAwait(false);
            contactIds.AddRange(placeContactIds);
        }
        var contacts = await contactIds
            .Distinct()
            .Select(id => contactsBackend.Get(userId, id, cancellationToken))
            .Collect(ApiConstants.Concurrency.Low, cancellationToken)
            .ConfigureAwait(false);
        // A contact turns Regular once its owner posts there; ListIds also returns fake contacts
        // for the Announcements chat and a place's public chats, which are never Regular
        return contacts
            .Where(c => c.IsRegular)
            .OrderByDescending(c => c.TouchedAt)
            .ToArray();
    }
}
