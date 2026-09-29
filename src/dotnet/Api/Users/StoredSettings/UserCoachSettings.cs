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
    public ApiMap<string, CoachLanguageLevel> Languages { get; init; } = new ();
    [DataMember, Key(6)]
    public ApiMap<string, CoachMetricKind> FocusByLanguage { get; init; } = new ();
    // "" = the language with the most words in the last 30 days
    [DataMember, Key(7)]
    public string SelectedLanguage { get; init; } = "";
    [DataMember, Key(8)]
    public bool AreMarksEnabled { get; init; } = true;
    [DataMember, Key(9)]
    public bool IsWeeklySummaryEnabled { get; init; } = true;

    public CoachLanguageLevel LevelOf(string? language)
        => language is not null && Languages.TryGetValue(Language.GetIsoCode(language), out var level)
            ? level
            : CoachLanguageLevel.Native;
}
