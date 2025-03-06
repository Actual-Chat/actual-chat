using ActualChat.Contacts;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch;

public static class ContactExt
{
    public static IndexedUserContact ToIndexedUserContact(this Contact contact)
        => new () {
            Id = contact.Id,
            Name = contact.PeerRename ?? "", // TODO(FC): Store both fields: PeerContactName and ExternalContactName
        };
}
