using ActualChat.Commands;

namespace ActualChat.Chat.Events;

[DataContract]
public record TextEntryChangedEvent(
    [property: DataMember] ChatEntry Entry,
    [property: DataMember] AuthorFull Author,
    [property: DataMember] ChangeKind ChangeKind
    ) : IEvent;
