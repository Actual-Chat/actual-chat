using ActualChat.Commands;

namespace ActualChat.Chat.Events;

[DataContract]
public record ReactionChangedEvent(
    [property: DataMember] ChatEntry Entry,
    [property: DataMember] AuthorFull Author,
    [property: DataMember] AuthorFull ReactionAuthor,
    [property: DataMember] string Emoji,
    [property: DataMember] ChangeKind ChangeKind
) : IEvent;
