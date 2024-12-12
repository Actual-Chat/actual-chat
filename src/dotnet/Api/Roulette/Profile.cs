using ActualChat.Users;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Roulette;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public partial record Profile([property: DataMember, MemoryPackOrder(0)] Symbol Id)
    : IHasId<Symbol>, IRequirementTarget
{
    public static readonly Profile None = new (Symbol.Empty) {
        Avatar = Avatar.None,
        Preferences = ProfilePreferences.None
    };

    public static readonly Profile Loading = new (Symbol.Empty) {
        Avatar = Avatar.Loading,
        Preferences = ProfilePreferences.None
    }; // Should differ by ref. From None

    [DataMember, MemoryPackOrder(1)] public Avatar Avatar { get; init; } = null!;
    [DataMember, MemoryPackOrder(3)] public ProfilePreferences Preferences { get; init; } = null!;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    public static Profile Create(Avatar avatar, ProfilePreferences? profilePreferences)
    {
        if (profilePreferences is not null &&  profilePreferences.Id != avatar.Id)
            throw new ArgumentOutOfRangeException(nameof(profilePreferences));

        return new Profile(avatar.Id) {
            Avatar = avatar,
            Preferences = profilePreferences ?? new ProfilePreferences(avatar.Id)
        };
    }
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public partial record ProfilePreferences(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    public static readonly ProfilePreferences None = new (Symbol.Empty);

    [DataMember, MemoryPackOrder(2)] public Preferences Preferences { get; init; } = Preferences.Empty;
}

public enum Gender { NotSpecified, Male, Female, Other }

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record Interest([property: DataMember, MemoryPackOrder(0)] string Code);

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public partial record Preferences
{
    public static readonly Preferences Empty = new ();

    [DataMember, MemoryPackOrder(0)] public Country Country { get; init; } = Country.NotSpecified;
    [DataMember, MemoryPackOrder(1)] public Gender Gender { get; init; } = Gender.NotSpecified;
    [DataMember, MemoryPackOrder(2)] public ImmutableArray<Language> Languages { get; init; } = ImmutableArray<Language>.Empty;
    [DataMember, MemoryPackOrder(3)] public ImmutableArray<Interest> Interests { get; init; } = ImmutableArray<Interest>.Empty;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty =>
        Country == Country.NotSpecified
        && Gender == Gender.NotSpecified
        && Languages.Length == 0
        && Interests.Length == 0;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsSufficientForFiltering
        => !IsEmpty && Languages.Length > 0;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public partial record ChatCandidate([property: DataMember, MemoryPackOrder(0)] Profile Profile)
{
}
