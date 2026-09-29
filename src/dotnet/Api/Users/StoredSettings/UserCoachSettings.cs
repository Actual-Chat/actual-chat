using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Speech-coach preferences: whether live tips and inline marking are on, and how often a tip may fire.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record UserCoachSettings
    : StoredSettings, IHasOrigin, IHasKvasKey<UserCoachSettings>
{
    public static string KvasKey => nameof(UserCoachSettings);

    [DataMember, Key(0)]
    public string Origin { get; init; } = "";
    [DataMember, Key(1)]
    public bool IsCoachingEnabled { get; init; }
    [DataMember, Key(2)]
    public bool AreLiveTipsEnabled { get; init; } = true;
    [DataMember, Key(3)]
    public TimeSpan TipInterval { get; init; } = TimeSpan.FromMinutes(5);
    [DataMember, Key(4)]
    public bool SkipPeerChats { get; init; }
    [DataMember, Key(5)]
    public ApiMap<string, CoachLanguageLevel> Languages { get => field ?? new (); init; } = new ();
    [DataMember, Key(6)]
    public ApiMap<string, CoachMetricKind> FocusByLanguage { get => field ?? new (); init; } = new ();
    // "" = the language with the most words in the last 30 days
    [DataMember, Key(7)]
    public string SelectedLanguage { get => field ?? ""; init; } = "";
    [DataMember, Key(8)]
    // Negative flags: a blob written before these keys reads them as false, which keeps the feature on
    public bool AreMarksDisabled { get; init; }
    [DataMember, Key(9)]
    public bool IsWeeklySummaryDisabled { get; init; }
    // Chats and places the user switched coaching off in, for the settings list
    [DataMember, Key(10)]
    public ApiArray<ChatId> SwitchedOff { get; init; }

    public CoachLanguageLevel LevelOf(string? language)
        => language is not null && Languages.TryGetValue(Language.GetIsoCode(language), out var level)
            ? level
            : CoachLanguageLevel.Native;
}
