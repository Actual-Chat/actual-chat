namespace ActualChat.Users;

/// <summary>
/// A user's usage totals. <see cref="Friends"/> is read live from Contacts, the rest is the sum of
/// the user's <see cref="UsageDay"/> rows.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UsageSummary
{
    public static readonly UsageSummary None = new();

    [DataMember, Key(0)] public int ActiveDays { get; init; }
    [DataMember, Key(1)] public long SpeechMs { get; init; }
    [DataMember, Key(2)] public int SpeechEntries { get; init; }
    [DataMember, Key(3)] public int Messages { get; init; }
    [DataMember, Key(4)] public int LiveSessions { get; init; }
    [DataMember, Key(5)] public int Friends { get; init; }
    [DataMember, Key(6)] public Moment? FirstActiveDay { get; init; }
    [DataMember, Key(7)] public Moment? LastActiveDay { get; init; }
}
