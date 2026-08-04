namespace ActualChat.Chat;

/// <summary>
/// Query parameters for listing changed chat entries by version range.
/// </summary>
[DataContract, MessagePackObject]
public partial record ChangedEntriesQuery
{
    [DataMember, Key(0)] public long MinVersion { get; init; }
    [DataMember, Key(1)] public long MaxVersion { get; init; } = long.MaxValue;
    [DataMember, Key(2)] public long LastLocalId { get; init; }
    [DataMember, Key(3)] public ChatId ChatId { get; init; } = null!;
    [DataMember, Key(4)] public int Limit { get; init; }
    [DataMember, Key(5)] public bool RequireAttachments { get; init; }
}
