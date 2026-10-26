using ActualChat.Attributes;
using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// The milestones each user reached: detected here from the other services' events, or reached through
/// <see cref="OnReach"/> by any server code that sees one happen.
/// </summary>
[BackendService(nameof(HostRole.UsersBackend), ServiceMode.Distributed)]
public interface IUserMilestonesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<UserMilestone>> List(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<MilestoneDelivery>> ListDeliveries(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<UserAnalyticsClient>> ListAnalyticsClients(UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<UserMilestone?> OnReach(UserMilestonesBackend_Reach command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSetDeliveries(UserMilestonesBackend_SetDeliveries command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSetAnalyticsClient(UserMilestonesBackend_SetAnalyticsClient command, CancellationToken cancellationToken);

    [EventHandler]
    Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
}

/// <summary>
/// Records the milestone unless the user reached it before; returns null in that case.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UserMilestonesBackend_Reach(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] MilestoneKind Kind,
    [property: DataMember, Key(2)] Moment ReachedAt
) : ICommand<UserMilestone?>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}

/// <summary>
/// Upserts delivery rows; written only by the user's <c>MilestoneDeliveryFlow</c>.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UserMilestonesBackend_SetDeliveries(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] ApiArray<MilestoneDelivery> Deliveries
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UserMilestonesBackend_SetAnalyticsClient(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] AnalyticsClientKind Kind,
    [property: DataMember, Key(2)] string ClientId,
    [property: DataMember, Key(3)] Moment SeenAt
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}
