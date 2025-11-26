using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record IndexingCursor(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] string Id,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] long Version
) : IComparable<IndexingCursor>
{
    public int CompareTo(IndexingCursor? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (ReferenceEquals(null, other)) return 1;
        var versionComparison = Version.CompareTo(other.Version);
        if (versionComparison != 0) return versionComparison;
        return string.Compare(Id, other.Id, StringComparison.Ordinal);
    }

    public static bool operator <(IndexingCursor? left, IndexingCursor? right) => Comparer<IndexingCursor>.Default.Compare(left, right) < 0;
    public static bool operator >(IndexingCursor? left, IndexingCursor? right) => Comparer<IndexingCursor>.Default.Compare(left, right) > 0;
    public static bool operator <=(IndexingCursor? left, IndexingCursor? right) => Comparer<IndexingCursor>.Default.Compare(left, right) <= 0;
    public static bool operator >=(IndexingCursor? left, IndexingCursor? right) => Comparer<IndexingCursor>.Default.Compare(left, right) >= 0;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public abstract partial class NewIndexingFlow<TItem> : Flow<Unit>
{
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual int BatchSize { get; } = 100;
    [IgnoreDataMember, MemoryPackIgnore]
    protected virtual TimeSpan PollInterval { get; } = TimeSpan.FromMinutes(1);
    [IgnoreDataMember, MemoryPackIgnore]
    protected abstract int CurrentFlowSetVersion { get; }

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public IndexingCursor? Cursor { get; protected set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public long TotalProcessed { get; protected set; }
    [DataMember(Order = 2), MemoryPackOrder(2)]
    public long FlowSetVersion { get; protected set; }

    protected abstract Task<IReadOnlyList<TItem>> GetBatch(IndexingCursor? cursor, int limit, CancellationToken cancellationToken);
    protected abstract Task ProcessBatch(IReadOnlyList<TItem> batch, CancellationToken cancellationToken);
    protected abstract IndexingCursor GetCursor(TItem item);

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        // Re-indexing check
        if (FlowSetVersion < CurrentFlowSetVersion) {
            Console.Log($"Re-indexing started: v.{FlowSetVersion} -> v.{CurrentFlowSetVersion}");
            Cursor = null;
            FlowSetVersion = CurrentFlowSetVersion;
        }

        while (true) {
            var batch = await GetBatch(Cursor, BatchSize, cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0) {
                Console.Log($"No more items. Sleeping for {PollInterval.ToShortString()}");
                Runtime.ScheduleResumeIn(PollInterval);
                return;
            }

            await ProcessBatch(batch, cancellationToken).ConfigureAwait(false);

            var lastItem = batch[^1];
            Cursor = GetCursor(lastItem);
            TotalProcessed += batch.Count;
            Console.Log($"Processed {batch.Count} items. Total: {TotalProcessed}. Cursor: {Cursor}");

            if (batch.Count < BatchSize) {
                Console.Log($"Partial batch ({batch.Count} < {BatchSize}). Sleeping for {PollInterval.ToShortString()}");
                Runtime.ScheduleResumeIn(PollInterval);
                return;
            }

            // If we processed a full batch, we continue immediately (or yield to avoid starvation if needed)
            // But since FlowRuntime handles execution, we can just loop.
            // However, to allow state persistence (checkpoints), we might want to return occasionally?
            // FlowRuntime commits automatically if we return. If we loop forever, we never commit.
            // So we should probably return to commit progress, but schedule immediate resume.

            Runtime.ScheduleResumeIn(TimeSpan.Zero);
            return;
        }
    }
}
