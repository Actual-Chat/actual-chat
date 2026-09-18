using System.Text;

namespace ActualChat.WebHooks;

public enum WebHookScope { Chat = 0, Place = 1, User = 2 }
public enum WebHookKind { Outgoing = 0, Incoming = 1 }
public enum WebHookDisabledReason { None = 0, Manual = 1, DeliveryFailures = 2, UnsafeUrl = 3 }
public enum WebHookDeliveryStatus { Pending = 0, Succeeded = 1, Failed = 2, Abandoned = 3 }

[Flags]
public enum WebHookEvents : long
{
    None = 0,
    MessagePosted = 1 << 0, MessageEdited = 1 << 1, MessageRemoved = 1 << 2,
    ReactionAdded = 1 << 3, ReactionRemoved = 1 << 4,
    MemberJoined = 1 << 5, MemberLeft = 1 << 6,
    ChatUpdated = 1 << 7, ChatCreated = 1 << 8, ChatArchived = 1 << 9,
    PlaceUpdated = 1 << 10, PlaceMemberJoined = 1 << 11, PlaceMemberLeft = 1 << 12,
    Notification = 1 << 13,
    Ping = 1 << 14,
    Messages = MessagePosted | MessageEdited | MessageRemoved,
    Reactions = ReactionAdded | ReactionRemoved,
    Members = MemberJoined | MemberLeft,
    ChatChanges = ChatUpdated | ChatCreated | ChatArchived,
    PlaceChanges = PlaceUpdated | PlaceMemberJoined | PlaceMemberLeft,
}

public static class WebHookEventsExt
{
    public static string ToEventType(this WebHookEvents single)
    {
        // "MessagePosted" -> "message.posted"; "PlaceMemberJoined" -> "place.member.joined"
        var name = single.ToString();
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++) {
            if (i > 0 && char.IsUpper(name[i]))
                sb.Append('.');
            sb.Append(char.ToLower(name[i]));
        }
        return sb.ToString();
    }
}
