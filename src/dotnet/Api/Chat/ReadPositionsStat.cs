using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ReadPositionsStat(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] long StartTrackingEntryLid,
    [property: DataMember, MemoryPackOrder(2)] ApiArray<AuthorReadPosition> TopReadPositions)
{
    public bool CanCalculateHasReadByAnotherAuthor(ChatEntry chatEntry)
        => chatEntry.LocalId >= StartTrackingEntryLid;

    public bool HasReadByAnotherAuthor(ChatEntry chatEntry, AuthorId ownAuthorId) {
        var entryId = chatEntry.Id.LocalId;
        foreach (var authorReadPosition in TopReadPositions)
            if (authorReadPosition.EntryLid >= entryId
                && !authorReadPosition.AuthorId.IsNone
                && authorReadPosition.AuthorId != ownAuthorId)
                return true;

        return false;
    }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AuthorReadPosition(
    [property: DataMember, MemoryPackOrder(0)] AuthorId AuthorId,
    [property: DataMember, MemoryPackOrder(1)] long EntryLid);
