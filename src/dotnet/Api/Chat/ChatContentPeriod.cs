namespace ActualChat.Chat;

// One month of indexed chat content: a UTC PeriodKey ("yyyy-MM") and the number of
// items in it. The skeleton that drives content-list scrolling.
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ChatContentPeriod
{
    public const int PageSize = 300;

    [DataMember, MemoryPackOrder(0), Key(0)] public required string PeriodKey { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public int ItemCount { get; init; }
}
