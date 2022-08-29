using ActualChat.Users;

namespace ActualChat.Chat;

public static class UserContactExt
{
    public static Symbol GetFullPeerChatId(this UserContact userContact)
        => ParsedChatId.FormatFullPeerChatId(userContact.OwnerUserId, userContact.TargetUserId);
}
