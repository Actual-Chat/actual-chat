using ActualChat.Attributes;
using ActualLab.Rpc;

namespace ActualChat.Users;

/// <summary>
/// Every ad or link that brought a user to the app, kept as soon as it appears, for new and existing users.
/// </summary>
[BackendService(nameof(HostRole.UsersBackend), ServiceMode.Distributed)]
public interface IUserAttributionsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<ApiArray<UserAttribution>> List(UserId userId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<UserAttribution?> OnAdd(UserAttributionsBackend_Add command, CancellationToken cancellationToken);
}

/// <summary>
/// Parses <see cref="Raw"/> and stores the attribution; one seen before is refreshed rather than duplicated.
/// Returns null when the value attributes nothing.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record UserAttributionsBackend_Add(
    [property: DataMember, Key(0)] UserId UserId,
    [property: DataMember, Key(1)] AttributionOrigin Origin,
    [property: DataMember, Key(2)] string Raw,
    [property: DataMember, Key(3)] Moment SeenAt
) : ICommand<UserAttribution?>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => UserId.ShardKey;
}
