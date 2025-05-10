using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Roulette;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public partial record ProfilePreferences(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    public static readonly ProfilePreferences None = new (Symbol.Empty);

    [DataMember, MemoryPackOrder(3)] public Preferences Preferences { get; init; } = Preferences.Empty;

    public ProfilePreferences WithMissingPropertiesFrom(ProfilePreferences? other)
        => other == null ? this
            : this with { Preferences = other.Preferences };

    // This record relies on referential equality
    public virtual bool Equals(ProfilePreferences? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record ProfilePreferencesFull(
    [property: DataMember, MemoryPackOrder(2)] UserId UserId,
    Symbol Id,
    long Version = 0
) : ProfilePreferences(Id, Version)
{
    public ProfilePreferences ToProfilePreferences()
        => new (Id, Version) {
            Preferences = Preferences,
        };

    public ProfilePreferencesFull WithMissingPropertiesFrom(ProfilePreferencesFull? other)
        => (ProfilePreferencesFull) base.WithMissingPropertiesFrom(other);
    public new ProfilePreferencesFull WithMissingPropertiesFrom(ProfilePreferences? other)
        => (ProfilePreferencesFull) base.WithMissingPropertiesFrom(other);

    // This record relies on referential equality
    public bool Equals(ProfilePreferencesFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
