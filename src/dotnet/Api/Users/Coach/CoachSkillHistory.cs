using ActualChat.Chat;

namespace ActualChat.Users;

public enum CoachHistoryPeriod { Day, Week, Month }

[DataContract, MessagePackObject]
public sealed partial record CoachSkillHistory
{
    public static readonly CoachSkillHistory None = new();

    [DataMember, Key(0)] public Range<Moment> Range { get; init; }
    [DataMember, Key(1)] public CoachSummary Summary { get; init; } = CoachSummary.None;
    [DataMember, Key(2)] public ApiArray<CoachSkillDay> Days { get; init; } = ApiArray<CoachSkillDay>.Empty;
    [DataMember, Key(3)] public CoachPaceDetails Pace { get; init; } = CoachPaceDetails.None;
    [DataMember, Key(4)] public ApiArray<CoachChip> Words { get; init; } = ApiArray<CoachChip>.Empty;
    [DataMember, Key(5)] public double? Value { get; init; }
    [DataMember, Key(6)] public long MeasuredWords { get; init; }
    [DataMember, Key(7)] public double MeasuredSeconds { get; init; }
    [DataMember, Key(8)] public int MinimumWords { get; init; }
}

[DataContract, MessagePackObject]
public sealed partial record CoachSkillDay(
    [property: DataMember, Key(0)] Moment Day,
    [property: DataMember, Key(1)] double Value,
    [property: DataMember, Key(2)] long MeasuredWords,
    [property: DataMember, Key(3)] double MeasuredSeconds,
    [property: DataMember, Key(4)] int GapDays);

public static class CoachHistoryRanges
{
    public static Range<Moment> Get(CoachHistoryPeriod period, Moment anchor, Moment now)
    {
        if (anchor > now)
            throw new ArgumentOutOfRangeException(nameof(anchor));

        var start = Start(period, anchor);
        var end = Move(period, start, 1);
        return new Range<Moment>(start, end > now ? now : end);
    }

    public static Moment Move(CoachHistoryPeriod period, Moment anchor, int offset)
    {
        var start = Start(period, anchor);
        return period switch {
            CoachHistoryPeriod.Day => start + TimeSpan.FromDays(offset),
            CoachHistoryPeriod.Week => start + TimeSpan.FromDays(7 * offset),
            CoachHistoryPeriod.Month => new Moment(start.ToDateTime().AddMonths(offset)),
            _ => throw new ArgumentOutOfRangeException(nameof(period)),
        };
    }

    public static Moment Start(CoachHistoryPeriod period, Moment anchor)
    {
        var day = UsageDay.DayOf(anchor);
        var date = day.ToDateTime();
        return period switch {
            CoachHistoryPeriod.Day => day,
            CoachHistoryPeriod.Week => CoachWeek.StartOf(day),
            CoachHistoryPeriod.Month => new Moment(new DateTime(date.Year, date.Month, 1,
                0, 0, 0, DateTimeKind.Utc)),
            _ => throw new ArgumentOutOfRangeException(nameof(period)),
        };
    }
}
