using ActualChat.Users;

namespace ActualChat.Chat;

public static class ContactExt
{
    public static Symbol GetFullPeerChatId(this Contact contact)
        => ParsedChatId.FormatFullPeerChatId(contact.OwnerUserId, contact.TargetUserId);
}
