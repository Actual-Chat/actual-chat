using ActualChat.Contacts;
using ActualChat.Users;

namespace ActualChat.Testing.Host;

public static class ContactOperations
{
    public static Task<Contact[]> CreatePeerContacts(
        this IWebTester tester,
        Account owner,
        params Account[] others)
        => others.Select(x => CreatePeerContact(tester, owner, x)).Collect();

    public static Task<Contact> CreatePeerContact(
        this IWebTester tester,
        Account owner,
        Account other)
    {
        var id = new ContactId(owner.Id, new PeerChatId(owner.Id, other.Id));
        var cmd = new Contacts_Change(tester.Session, id, null, Change.Create(new Contact(id)));
        return tester.Commander.Call(cmd).Require();
    }
}
