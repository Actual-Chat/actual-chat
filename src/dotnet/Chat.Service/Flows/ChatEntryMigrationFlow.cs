using ActualChat.Flows;

namespace ActualChat.Chat.Flows;

/// <summary>
/// Retired: the legacy audio entries were migrated to the media table long ago.
/// The type stays so that the persisted flow state still resolves.
/// </summary>
[Flow(DataVersion = 1, DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public partial class ChatEntryMigrationFlow : Flow<(Moment, long, long)>
{
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0)]
    public string? LastProcessedChatId { get; set; }
    [DataMember(Order = 1), MemoryPackOrder(1), Key(1)]
    public long LastProcessedLocalId { get; set; }
    [DataMember(Order = 2), MemoryPackOrder(2), Key(2)]
    public long MigratedEntryCount { get; set; }
    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public long MigratedChatCount { get; set; }
    [DataMember(Order = 4), MemoryPackOrder(4), Key(4)]
    public long TotalEntryCount { get; set; }
    [DataMember(Order = 5), MemoryPackOrder(5), Key(5)]
    public long TotalChatCount { get; set; }

    protected override ValueTask Resume(CancellationToken cancellationToken)
    {
        SetResult((Hub.Clocks.SystemClock.Now, MigratedChatCount, MigratedEntryCount));
        return ValueTask.CompletedTask;
    }
}
