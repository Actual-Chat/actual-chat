namespace ActualChat.Contacts;

public static class ContactsExt
{
    // ListXxx

    public static ValueTask<List<Contact>> ListChatContacts(
        this IContacts contacts,
        Session session,
        CancellationToken cancellationToken)
        => contacts.ListContacts(session, c => c.Account == null, cancellationToken);

    public static ValueTask<List<Contact>> ListUserContacts(
        this IContacts contacts,
        Session session,
        CancellationToken cancellationToken)
        => contacts.ListContacts(session, c => c.Account != null, cancellationToken);

    public static ValueTask<List<Contact>> ListContacts(
        this IContacts contacts,
        Session session,
        CancellationToken cancellationToken)
        => contacts.ListContacts(session, null, cancellationToken);

    public static async ValueTask<List<Contact>> ListContacts(
        this IContacts contacts,
        Session session,
        Func<Contact, bool>? filter,
        CancellationToken cancellationToken)
    {
        var contactIds = await contacts.ListIds(session, cancellationToken).ConfigureAwait(false);
        var candidates = await contactIds
            .Select(cid => contacts.Get(session, cid, default))
            .Collect()
            .ConfigureAwait(false);
        if (filter == null)
            return candidates.SkipNullItems().ToList();

        return candidates.Where(c => c != null && filter.Invoke(c)).ToList()!;
    }
}
