using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Backend service for the per-user cloned-voice pool used by dubbing.
/// </summary>
public interface IUserVoicesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<UserVoice?> Get(UserId userId, CancellationToken cancellationToken);

    // Non-compute methods
    // The sweeper's snapshot
    Task<ApiArray<UserVoice>> ListActive(CancellationToken cancellationToken);

    [CommandHandler]
    Task<UserVoice?> OnChange(UserVoicesBackend_Change command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to create, update, or remove a user's cloned-voice record.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UserVoicesBackend_Change(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] long? ExpectedVersion,
    [property: DataMember, Key(2)] Change<UserVoiceDiff> Change
) : ICommand<UserVoice?>, IBackendCommand, IHasShardKey<UserId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}
