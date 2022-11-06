using ActualChat.Commands;

namespace ActualChat.Chat.Events;

[DataContract]
public record ReactionChangedEvent(
    [property: DataMember(Order = 0)] string ChatEntryId,
    [property: DataMember(Order = 1)] string AuthorId,
    [property: DataMember(Order = 2)] string OriginalMessageAuthorUserId,
    [property: DataMember(Order = 3)] string Emoji,
    [property: DataMember(Order = 4)] string OriginalMessageContent,
    [property: DataMember(Order = 5)] ChangeKind ChangeKind
) : IEvent;
