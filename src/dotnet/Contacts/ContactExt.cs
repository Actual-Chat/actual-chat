using ActualChat.Chat;

namespace ActualChat.Contacts;

public static class ContactExt
{
    public static Symbol GetFullPeerChatId(this Contact contact)
        => ParsedChatId.FormatFullPeerChatId(contact.OwnerId, contact.Account!.Id);
}
