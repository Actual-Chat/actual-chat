namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public partial record ReadPositionsStat(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] long StartTrackingEntryLid,
    [property: DataMember, Key(2)] AuthorReadPosition[] TopReadPositions)
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
                && authorReadPosition.AuthorId != ownAuthorId)
                return true;

        return false;
    }
}

[DataContract, MessagePackObject]
public partial record AuthorReadPosition(
    [property: DataMember, Key(0)] AuthorId AuthorId,
    [property: DataMember, Key(1)] long EntryLid);
