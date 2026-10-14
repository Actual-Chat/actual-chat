namespace ActualChat.Live;

/// <summary>
/// The evolving summary fields written into the open <see cref="LiveConversation"/> by the live summary loop.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record LiveConversationSummary
{
    [DataMember(Order = 0), Key(0)]
    public string Title { get; init; } = "";
    [DataMember(Order = 1), Key(1)]
    public string Description { get; init; } = "";
    [DataMember(Order = 2), Key(2)]
    public string Summary { get; init; } = "";
    [DataMember(Order = 3), Key(3)]
    public long SummaryEndLid { get; init; }
    [DataMember(Order = 4), Key(4)]
    public int SummarizedEntryCount { get; init; }
    [DataMember(Order = 5), Key(5)]
    public IReadOnlyList<AuthorId> AuthorIds { get; init; } = [];
    [DataMember(Order = 6), Key(6)]
    public bool IsExpandedByDefault { get; init; }
}
