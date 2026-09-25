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
}
