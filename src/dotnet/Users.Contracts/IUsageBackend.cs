using ActualChat.Attributes;
using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Per-user usage: an append-only event log and its per-day rollup, fed by the other services' events.
/// </summary>
[BackendService(nameof(HostRole.UsersBackend), ServiceMode.Distributed)]
public interface IUsageBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<UsageSummary> GetSummary(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<UsageDay>> ListDays(UserId userId, Range<Moment> range, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task OnRecord(UsageBackend_Record command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRebuildDays(UsageBackend_RebuildDays command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnLiveSessionEndedEvent(LiveSessionEndedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnContactChangedEvent(ContactChangedEvent eventCommand, CancellationToken cancellationToken);
}

/// <summary>
/// Appends events to a user's log; ones already recorded (same kind and source id) are skipped,
/// so redelivery is harmless.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UsageBackend_Record(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] ApiArray<UsageEvent> Events
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}

/// <summary>
/// Recomputes the user's day rows from the event log.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UsageBackend_RebuildDays(
    [property: DataMember, Key(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}
