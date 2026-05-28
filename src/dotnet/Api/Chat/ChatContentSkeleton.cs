namespace ActualChat.Chat;

// Skeleton response from IChats.GetContentPeriods: a newest-first batch of periods
// plus a pagination cursor. NextPeriodKey is opaque; when non-null, the caller can
// continue with GetContentPeriods(beforePeriodKey: NextPeriodKey) to load older
// periods. Currently the backend always returns the full history in one batch and
// sets NextPeriodKey to null — the field is here so the contract supports lazy
// paging in the future without a breaking change.
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ChatContentSkeleton
{
    [DataMember, MemoryPackOrder(0), Key(0)] public required ChatContentPeriod[] Periods { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public string? NextPeriodKey { get; init; }
}
