using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ReadPositionsStat(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] long StartTrackingEntryLid,
    [property: DataMember, MemoryPackOrder(2)] ApiArray<AuthorReadPosition> TopReadPositions)
{
    public bool CanCalculateHasReadByAnotherAuthor(ChatEntry chatEntry)
        => CanCalculateHasReadByAnotherAuthor(chatEntry.Id);

    public bool CanCalculateHasReadByAnotherAuthor(ChatEntryId chatEntryId)
        => chatEntryId.LocalId >= StartTrackingEntryLid;

    public bool HasReadByAnotherAuthor(ChatEntry chatEntry, AuthorId ownAuthorId)
        => HasReadByAnotherAuthor(chatEntry.Id, ownAuthorId);

    public bool HasReadByAnotherAuthor(ChatEntryId chatEntryId, AuthorId ownAuthorId) {
        var entryLid = chatEntryId.LocalId;
        foreach (var authorReadPosition in TopReadPositions)
            if (authorReadPosition.EntryLid >= entryLid
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
