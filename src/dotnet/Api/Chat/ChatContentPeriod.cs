namespace ActualChat.Chat;

// One slice of indexed chat content the UI can fetch in one go. PeriodKey is opaque
// to the client: the backend defines its format (currently UTC "yyyy-MM") and may
// evolve it without contract changes. PageCount tells the UI how many pages exist
// in this period (page indices 0..PageCount-1, page 0 holding the oldest items).
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record ChatContentPeriod
{
    public const int PageSize = 300;

    [DataMember, MemoryPackOrder(0), Key(0)] public required string PeriodKey { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public int PageCount { get; init; }
}
