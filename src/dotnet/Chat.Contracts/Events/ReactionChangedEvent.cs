using ActualChat.Commands;

namespace ActualChat.Chat.Events;

[DataContract]
public record ReactionChangedEvent(
    [property: DataMember] Reaction Reaction,
    [property: DataMember] ChatEntry Entry,
    [property: DataMember] AuthorFull Author,
    [property: DataMember] AuthorFull ReactionAuthor,
    [property: DataMember] ChangeKind ChangeKind
) : IEvent;
