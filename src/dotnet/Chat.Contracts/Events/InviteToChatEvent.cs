using ActualChat.Commands;

namespace ActualChat.Chat.Events;

[DataContract]
public record InviteToChatEvent(
    [property: DataMember] string ChatId,
    [property: DataMember] string UserId
    ) : IEvent;
