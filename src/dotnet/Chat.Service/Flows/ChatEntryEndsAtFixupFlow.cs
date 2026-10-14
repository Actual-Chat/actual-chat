using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Retired: the text entries with a missing EndsAt were fixed long ago.
/// The type stays so that the persisted flow state still resolves.
/// </summary>
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class ChatEntryEndsAtFixupFlow : Flow<(Moment, long)>
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public string? LastProcessedEntryId { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public long FixedCount { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public long SkippedCount { get; set; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public long TotalScanned { get; set; }

    protected override ValueTask Resume(CancellationToken cancellationToken)
    {
        SetResult((Hub.Clocks.SystemClock.Now, FixedCount));
        return ValueTask.CompletedTask;
    }
}
