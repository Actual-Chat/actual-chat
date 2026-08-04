namespace ActualChat.Chat;

// Skeleton response from IChats.GetContentPeriods: a newest-first batch of periods
// plus an opaque pagination cursor. When NextPeriodKey is non-null the caller can
// continue with GetContentPeriods(beforePeriodKey: NextPeriodKey) to load older
// periods. Periods can be empty while NextPeriodKey is still set — older history
// exists, just not within this page's window.
[DataContract, MessagePackObject]
public sealed partial record ChatContentSkeleton
{
    [DataMember, Key(0)] public required ChatContentPeriod[] Periods { get; init; }
    [DataMember, Key(1)] public string? NextPeriodKey { get; init; }
}
