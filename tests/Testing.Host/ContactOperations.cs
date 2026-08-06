using ActualChat.Contacts;

namespace ActualChat.Testing.Host;

public static class ContactOperations
{
    public static Task<Contact[]> CreatePeerContacts(
        this IWebTester tester,
        Account owner,
        params Account[] others)
        => others.Select(x => CreatePeerContact(tester, owner, x)).Collect(Environment.ProcessorCount / 2);

    public static Task<Contact> CreatePeerContact(
        this IWebTester tester,
        Account owner,
        Account other)
    {
        var id = ContactId.NewUser(owner.Id, other.Id);
        var cmd = new Contacts_Change(tester.Session, id, null, Change.Create(new Contact(id)));
        return tester.Commander.Call(cmd).Require();
    }
}
