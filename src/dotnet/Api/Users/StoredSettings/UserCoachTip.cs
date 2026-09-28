using ActualChat.Kvas;

namespace ActualChat.Users;

public enum CoachTipKind
{
    None = 0,
    SlowDown = 1,
    SpeedUp = 2,
    Filler = 3,
    WeakWord = 4,
}

/// <summary>
/// The user's latest live coaching tip and when the last one fired; the client shows it in its
/// chat until dismissed or replaced.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserCoachTip : StoredSettings, IHasOrigin, IHasKvasKey<UserCoachTip>
{
    public static string KvasKey => nameof(UserCoachTip);

    [DataMember, Key(0)]
    public string Origin { get; init; } = "";
    [DataMember, Key(1)]
    public CoachTipKind Kind { get; init; }
    [DataMember, Key(2)]
    public ChatId ChatId { get; init; }
    [DataMember, Key(3)]
    public long EntryLid { get; init; }
    [DataMember, Key(4)]
    public string Word { get; init; } = "";
    [DataMember, Key(5)]
    public int Count { get; init; }
    [DataMember, Key(6)]
    public int Wpm { get; init; }
    [DataMember, Key(7)]
    public ApiArray<string> Synonyms { get; init; }
    [DataMember, Key(8)]
    public Moment ShownAt { get; init; }
    [DataMember, Key(9)]
    public bool IsDismissed { get; init; }
    [DataMember, Key(10)]
    public Moment LastTipAt { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsPending => Kind != CoachTipKind.None && !IsDismissed;
}
