using ActualChat.Kvas;

namespace ActualChat.Users;

public enum CoachTipKind
{
    None = 0,
    SlowDown = 1,
    SpeedUp = 2,
    Filler = 3,
    WeakWord = 4,
    Clean = 5,
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
    // The good pace band the card quotes next to a pace tip, in wpm
    [DataMember, Key(11)]
    public int PaceSlowWpm { get; init; }
    [DataMember, Key(12)]
    public int PaceFastWpm { get; init; }
    // The window the count was taken over, in minutes, so the card says what triggered it
    [DataMember, Key(13)]
    public int WindowMinutes { get; init; }
    // When each word was last tipped; the policy's per-word cooldown reads it
    [DataMember, Key(14)]
    public ApiMap<string, Moment> WordTipAt { get; init; } = new ();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsPending => Kind != CoachTipKind.None && !IsDismissed;
}
