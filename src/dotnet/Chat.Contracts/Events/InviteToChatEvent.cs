using ActualChat.Commands;

namespace ActualChat.Chat.Events;

[DataContract]
public record InviteToChatEvent(
    [property: DataMember(Order = 0)] string ChatId,
    [property: DataMember(Order = 1)] string UserId
    ) : IEvent;
