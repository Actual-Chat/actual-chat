using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

// Each sweep runs BatchesPerSweep batches, BatchPeriod apart; its arguments are "<key>,<BatchesPerSweep>"

[Flow(DelayQuanta = 0)]
[DataContract, MessagePackObject(true)]
public sealed partial class SimpleBatchedSweepFlow : BatchedSweepFlow
{
    public static readonly TimeSpan BatchPeriod = TimeSpan.FromMilliseconds(300);

    [DataMember(Order = 10), Key(10)]
    public int BatchIndex { get; set; }
    [DataMember(Order = 11), Key(11)]
    public int TotalBatchCount { get; set; }

    // Protected/internal methods

    protected override ValueTask StartSweep(CancellationToken cancellationToken)
    {
        BatchIndex = 0;
        return default;
    }

    protected override ValueTask<Moment?> RunBatch(CancellationToken cancellationToken)
    {
        var batchesPerSweep = int.Parse(Id.SplitArguments("", "1")[1]);
        BatchIndex++;
        TotalBatchCount++;
        Console.Log($"Batch {BatchIndex}/{batchesPerSweep}");
        Moment? nextBatchAt = BatchIndex < batchesPerSweep ? Hub.SystemNow + BatchPeriod : null;
        return ValueTask.FromResult(nextBatchAt);
    }
}
