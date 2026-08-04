namespace ActualChat.Live;

/// <summary>
/// The evolving summary fields written into a <see cref="LiveSessionState"/> by the live summary loop.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record LiveSessionSummary
{
    [DataMember(Order = 0), Key(0)]
    public string Title { get; init; } = "";
    [DataMember(Order = 1), Key(1)]
    public string Description { get; init; } = "";
    [DataMember(Order = 2), Key(2)]
    public string Summary { get; init; } = "";
    [DataMember(Order = 3), Key(3)]
    public long EndEntryLid { get; init; }
    [DataMember(Order = 4), Key(4)]
    public int MessageCount { get; init; }
    [DataMember(Order = 5), Key(5)]
    public IReadOnlyList<AuthorId> AuthorIds { get; init; } = [];
    [DataMember(Order = 6), Key(6)]
    public bool IsExpandedByDefault { get; init; }
}
