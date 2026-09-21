namespace ActualChat.Users;

/// <summary>
/// A user's activity rolled up over one UTC day. Existence of the row is what makes the day active.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UsageDay(
    [property: DataMember, Key(0)] Moment Day,
    [property: DataMember, Key(1)] long SpeechMs,
    [property: DataMember, Key(2)] int SpeechEntries,
    [property: DataMember, Key(3)] int Messages,
    [property: DataMember, Key(4)] int LiveSessions,
    [property: DataMember, Key(5)] int ContactsAdded,
    [property: DataMember, Key(6)] int ContactsRemoved
)
{
    public static Moment DayOf(Moment moment)
        => moment.ToDateTime().Date;
}
