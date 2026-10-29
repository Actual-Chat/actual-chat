using ActualChat.Contacts;

namespace ActualChat.Testing.Host;

public static class ContactOperations
{
    public static Task<Contact[]> CreatePeerContacts(
        this IWebTester tester,
        Account owner,
        params Account[] others)
        => others.Select(x => CreatePeerContact(tester, owner, x)).Collect(Environment.ProcessorCount / 2);

    public static async Task<Contact> RenamePeerContact(
        this IWebTester tester,
        Account owner,
        Account other,
        string name)
    {
        // Saved the way PeerContactEditorModal saves it
        var contact = await tester.CreatePeerContact(owner, other).ConfigureAwait(false);
        var cmd = new Contacts_Change {
            Session = tester.Session,
            Id = contact.Id,
            ExpectedVersion = contact.Version,
            Change = Change.Update(contact with { PeerContactName = name }),
        };
        return await tester.Commander.Call(cmd).Require().ConfigureAwait(false);
    }

    public static Task<Contact> CreatePeerContact(
        this IWebTester tester,
        Account owner,
        Account other)
    {
        var id = ContactId.NewUser(owner.Id, other.Id);
        var cmd = new Contacts_Change {
            Session = tester.Session,
            Id = id,
            ExpectedVersion = null,
            Change = Change.Create(new Contact(id)),
        };
        return tester.Commander.Call(cmd).Require();
    }
}
