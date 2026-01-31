using ActualChat.Users;
using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Roulette;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public partial record Profile : IHasId<Symbol>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(0)] public required Symbol Id { get; init; }
    [DataMember, MemoryPackOrder(1)] public required Avatar Avatar { get; init; }
    [DataMember, MemoryPackOrder(3)] public required ProfilePreferences Preferences { get; init; }

    public static Profile New(Avatar avatar, ProfilePreferences? profilePreferences)
    {
        if (profilePreferences is not null &&  profilePreferences.Id != avatar.Id)
            throw new ArgumentOutOfRangeException(nameof(profilePreferences));

        return new Profile {
            Id = avatar.Id,
            Avatar = avatar,
            Preferences = profilePreferences ?? new ProfilePreferences(avatar.Id),
        };
    }

    // This record relies on referential equality
    public virtual bool Equals(Profile? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record ProfileFull : Profile
{
    [DataMember, MemoryPackOrder(2)] public required UserId UserId { get; init; }

    public Profile ToProfile()
        => New(Avatar, Preferences);

    // This record relies on referential equality
    public bool Equals(ProfileFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
