using ActualLab.Rpc;
using ActualLab.Versioning;
using MemoryPack;

namespace ActualChat.Chat;

public interface IUserLinksBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<UserLink?> Get(UserLinkId userLinkId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<UserLink?> OnChange(UserLinksBackend_Change command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record UserLinksBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] UserLinkId UserLinkId,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<UserLink> Change
) : ICommand<UserLink?>, IBackendCommand, IHasShardKey<UserLinkId>
{
    [IgnoreDataMember, MemoryPackIgnore]
    public UserLinkId ShardKey => UserLinkId;
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserLink(
    [property: DataMember, MemoryPackOrder(0)] UserLinkId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<UserLinkId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(3)] public UserLinkKind Kind { get; init; }
    [DataMember, MemoryPackOrder(4)] public string TargetId { get; init; } = "";

    // This record relies on referential equality
    public bool Equals(UserLink? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
