namespace ActualChat.Chat;

/// <summary>
/// An active import session, identified by its <see cref="ImportId"/>. Its <see cref="ChatId"/> is
/// the chat whose maintenance carries it: a group chat, or a Place root chat the Place's chats inherit.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ChatImportSession(
    [property: DataMember, Key(0)] ChatImportId ImportId)
{
    [DataMember, Key(1)] public UserId? StartedBy { get; init; }
    [DataMember, Key(2)] public Moment StartedAt { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ChatId => ImportId.ChatId;
}

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
