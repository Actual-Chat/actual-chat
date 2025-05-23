using ActualLab.Rpc;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Chat;

public interface IAliasBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Alias?> Get(AliasId aliasId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Alias?> OnChange(AliasBackend_Change command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record AliasBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] AliasId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<Alias> Change
) : ICommand<Alias?>, IBackendCommand, IHasShardKey<AliasId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public AliasId ShardKey => Id;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Alias(
    [property: DataMember, MemoryPackOrder(0)] AliasId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<AliasId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(3)] public AliasKind Kind { get; init; }
    [DataMember, MemoryPackOrder(4)] public string TargetId { get; init; } = "";

    // This record relies on referential equality
    public bool Equals(Alias? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
