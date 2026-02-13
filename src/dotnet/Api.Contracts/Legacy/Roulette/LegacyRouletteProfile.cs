namespace ActualChat.Roulette;

/// <summary>
/// Legacy Profile type for backwards compatibility with old clients.
/// Chat Roulette feature has been removed.
/// </summary>
[Obsolete("Chat Roulette feature has been removed.")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record LegacyRouletteProfile(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id
) : IHasId<Symbol>, IRequirementTarget
{
    // This record relies on referential equality
    public bool Equals(LegacyRouletteProfile? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
