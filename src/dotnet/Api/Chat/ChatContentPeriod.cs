namespace ActualChat.Chat;

/// <summary>
/// One month of indexed chat content: a UTC <see cref="PeriodKey"/> ("yyyy-MM") and the number
/// of <see cref="ChatContentItem"/>s in it. The skeleton that drives content-list scrolling.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ChatContentPeriod
{
    public const int PageSize = 300;

    [DataMember, MemoryPackOrder(0), Key(0)] public required string PeriodKey { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public int ItemCount { get; init; }
}
