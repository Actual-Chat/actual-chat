namespace ActualChat.Contacts;

public static class ContactsExt
{
    public static ValueTask<IEnumerable<Contact>> ListChatContacts(
        this IContacts contacts,
        Session session,
        CancellationToken cancellationToken)
        => contacts.ListContacts(session, c => c.Chat != null, cancellationToken);

    public static ValueTask<IEnumerable<Contact>> ListUserContacts(
        this IContacts contacts,
        Session session,
        CancellationToken cancellationToken)
        => contacts.ListContacts(session, c => c.Account != null, cancellationToken);

    public static ValueTask<IEnumerable<Contact>> ListContacts(
        this IContacts contacts,
        Session session,
        CancellationToken cancellationToken)
        => contacts.ListContacts(session, null, cancellationToken);

    public static async ValueTask<IEnumerable<Contact>> ListContacts(
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
            return candidates.SkipNullItems();

        return candidates.Where(c => c != null && filter.Invoke(c))!;
    }
}
