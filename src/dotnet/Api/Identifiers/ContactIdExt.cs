using System.Text;
using ActualChat.Hashing;

namespace ActualChat;

public static class ContactIdExt
{
    public static UserId GetOtherUserId(this ContactId id)
        => id.ChatId is PeerChatId peerChatId
            ? peerChatId.AnotherUserId(id.OwnerId)
            : throw StandardError.Constraint("Only peer chat contacts are allowed.");

    public static string Hash(string value)
        => value.ToLowerInvariant().Hash(Encoding.UTF8).SHA256().Base64();
}
