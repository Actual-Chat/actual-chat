using ActualChat.Commands;

namespace ActualChat.Chat.Events;

[DataContract]
public record AuthorChangedEvent(
    [property: DataMember] AuthorFull Author,
    [property: DataMember] AuthorFull? OldAuthor
) : EventCommand;
