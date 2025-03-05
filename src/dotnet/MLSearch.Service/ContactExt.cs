using ActualChat.Contacts;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch;

public static class ContactExt
{
    public static IndexedUserContact ToIndexedUserContact(this Contact contact)
        => new () {
            Id = contact.Id,
            Name = contact.PeerRename ?? "", // TODO(DF): Frol, can review how property should be filled in?
        };
}
