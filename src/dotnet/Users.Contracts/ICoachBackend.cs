using ActualChat.Attributes;
using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// The user side of the speech coach: a per-user log of the chat-side analyses keyed by source id,
/// day rows rebuilt from it, and the occurrences behind jump-to-audio.
/// </summary>
[BackendService(nameof(HostRole.UsersBackend), ServiceMode.Distributed)]
public interface ICoachBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<CoachDay>> ListDays(
        UserId userId, Range<Moment> dayRange, string? language, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<CoachOccurrence>> ListOccurrences(
        UserId userId, string word, Range<Moment> range, int limit, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task OnRecord(CoachBackend_Record command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRebuildDays(CoachBackend_RebuildDays command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnCoachEntryAnalyzedEvent(CoachEntryAnalyzedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnCoachConversationAnalyzedEvent(
        CoachConversationAnalyzedEvent eventCommand, CancellationToken cancellationToken);
}

/// <summary>
/// Appends the record to the user's log, or replaces the row with the same source id; a removal
/// deletes it. The record's day is rebuilt from the log either way.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record CoachBackend_Record(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] CoachRecord Record,
    [property: DataMember, Key(2)] bool IsRemoved
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record CoachBackend_RebuildDays(
    [property: DataMember, Key(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}
