using ActualChat.Kvas;

namespace ActualChat.Users;

[DataContract, MessagePackObject]
public sealed partial record UserCoachWeeklyNote : StoredSettings, IHasOrigin, IHasKvasKey<UserCoachWeeklyNote>
{
    public static string KvasKey => nameof(UserCoachWeeklyNote);

    [DataMember, Key(0)] public string Origin { get; init; } = "";
    [DataMember, Key(1)] public Moment WeekStart { get; init; }
    [DataMember, Key(2)] public int? ScoreDelta { get; init; }
    [DataMember, Key(3)] public CoachMetricKind? FocusKind { get; init; }
    [DataMember, Key(4)] public double? FocusDelta { get; init; }
    [DataMember, Key(5)] public ChatId BestChatId { get; init; }
    [DataMember, Key(6)] public long BestStartLid { get; init; }
    [DataMember, Key(7)] public bool IsSeen { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsPending => WeekStart != default && !IsSeen;
}
