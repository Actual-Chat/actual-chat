using ActualChat.Commands;

namespace ActualChat.Chat.Events;

[DataContract]
public record ChatChangedEvent(
    [property: DataMember] Chat Chat,
    [property: DataMember] Chat? OldChat
) : EventCommand;
