namespace ActualChat.Contacts;

/// <summary>
/// Query parameters for listing changed contacts by version range.
/// </summary>
[DataContract, MessagePackObject]
public partial record ChangedContactsQuery
{
    [DataMember, Key(2)] public required ContactId? LastId { get; init; }
    [DataMember, Key(3)] public required int Limit { get; init; }
    [DataMember, Key(0)] public long MinVersion { get; init; }
    [DataMember, Key(1)] public long MaxVersion { get; init; } = long.MaxValue;
}
