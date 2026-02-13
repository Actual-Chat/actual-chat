using ActualLab.Versioning;

namespace ActualChat.Roulette;

/// <summary>
/// Legacy RouletteUserSettings type for backwards compatibility with old clients.
/// Chat Roulette feature has been removed.
/// </summary>
[Obsolete("Chat Roulette feature has been removed.")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record RouletteUserSettings(
    [property: DataMember, MemoryPackOrder(0)] UserId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
) : IHasId<UserId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public bool IsEnabled { get; set; }

    // This record relies on referential equality
    public bool Equals(RouletteUserSettings? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
