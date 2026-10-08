namespace ActualChat.Chat;

public enum HistoryChangeKind
{
    RetentionChanged = 0,
    Wiped = 1,
}

/// <summary>
/// System entry recording who changed the chat's history retention or wiped its history.
/// <see cref="HistoryPeriod"/> is the new retention (<c>null</c> = off) or how far back
/// the wipe went (<c>null</c> = the whole history).
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record HistoryChangedEntry(ChatEntryId Id, long Version = 0) : SystemEntry(Id, Version)
{
    [DataMember, Key(20)] public AuthorId? TargetAuthorId { get; init; }
    [DataMember, Key(21)] public string TargetAuthorName { get; init; } = "";
    [DataMember, Key(22)] public HistoryChangeKind HistoryChange { get; init; }
    [DataMember, Key(23)] public TimeSpan? HistoryPeriod { get; init; }

    public HistoryChangedEntry() : this(null!, 0)
    { }
}
