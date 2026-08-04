using ActualLab.Rpc;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for managing chat and place aliases.
/// </summary>
public interface IAliasBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Alias?> Get(AliasId aliasId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Alias?> OnChange(AliasBackend_Change command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to create, update, or delete an alias.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record AliasBackend_Change(
    [property: DataMember, Key(0)] AliasId Id,
    [property: DataMember, Key(1)] long? ExpectedVersion,
    [property: DataMember, Key(2)] Change<Alias> Change
) : ICommand<Alias?>, IBackendCommand, IHasShardKey<AliasId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public AliasId ShardKey => Id;
}

/// <summary>
/// Represents a named alias pointing to a chat or place.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record Alias(
    [property: DataMember, Key(0)] AliasId Id,
    [property: DataMember, Key(1)] long Version = 0
    ) : IHasId<AliasId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(2)] public Moment CreatedAt { get; init; }
    [DataMember, Key(3)] public AliasKind Kind { get; init; }
    [DataMember, Key(4)] public string TargetId { get; init; } = "";

    // This record relies on referential equality
    public bool Equals(Alias? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
