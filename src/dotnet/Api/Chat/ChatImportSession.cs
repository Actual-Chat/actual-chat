namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed partial record ChatImportSession(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] string Id,
    [property: DataMember, Key(2)] UserId StartedBy,
    [property: DataMember, Key(3)] Moment StartedAt,
    [property: DataMember, Key(4)] bool IsActive);

[DataContract, MessagePackObject]
public sealed partial record ChatImportConsentSummary(
    [property: DataMember, Key(0)] int ConsentingCount,
    [property: DataMember, Key(1)] int NonConsentingCount,
    [property: DataMember, Key(2)] ApiArray<UserId> NonConsentingUserIds);

[DataContract, MessagePackObject]
public sealed partial record ChatImportEntry(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] Moment BeginsAt,
    [property: DataMember, Key(2)] string Content)
{
    [DataMember, Key(3)] public ApiArray<UploadId> UploadIds { get; init; }
    [DataMember, Key(4)] public long? RepliedEntryLid { get; init; }
}

[DataContract, MessagePackObject]
public sealed partial record ChatImportEntryResult(
    [property: DataMember, Key(0)] ChatEntryId? EntryId,
    [property: DataMember, Key(1)] ExceptionInfo Error);
