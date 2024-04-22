namespace ActualChat.Contacts;

public static class ContactsExt
{
    // ListXxx

    public static ValueTask<List<Contact>> ListUserContacts(
        this IContacts contacts,
        Session session,
        CancellationToken cancellationToken)
        => contacts.ListContacts(session, PlaceId.None, c => c.Account != null, cancellationToken);

    public static ValueTask<List<Contact>> ListContacts(
        this IContacts contacts,
        Session session,
        PlaceId placeId,
        CancellationToken cancellationToken)
        => contacts.ListContacts(session, placeId, null, cancellationToken);

    public static async ValueTask<List<Contact>> ListContacts(
        this IContacts contacts,
        Session session,
        PlaceId placeId,
        Func<Contact, bool>? filter,
        CancellationToken cancellationToken)
    {
        var contactIds = await contacts.ListIds(session, placeId, cancellationToken).ConfigureAwait(false);
        var candidates = await contactIds
            .Select(cid => contacts.Get(session, cid, default))
            .Collect() // Add concurrency
            .ConfigureAwait(false);
        if (filter == null)
            return candidates.SkipNullItems().ToList();

        return candidates.Where(c => c != null && filter.Invoke(c)).ToList()!;
    }
}
