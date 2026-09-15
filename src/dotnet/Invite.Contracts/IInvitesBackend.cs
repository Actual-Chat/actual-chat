using ActualLab.Rpc;

namespace ActualChat.Invite;

/// <summary>
/// Backend service for managing chat and place invitations.
/// </summary>
public interface IInvitesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Invite?> Get(string id, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Invite[]> GetAll(string searchKey, int minRemaining, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<InviteChatLinkPreview?> GetInviteChatLinkPreview(UserId accountId, string inviteId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Invite> OnGenerate(InvitesBackend_Generate command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Invite> OnUse(InvitesBackend_Use command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRevoke(InvitesBackend_Revoke command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to revoke an invitation.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record InvitesBackend_Revoke(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] string InviteId
) : ISessionCommand<Unit>, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ShardKey.New(InviteId);
}

/// <summary>
/// Command to use (accept) an invitation.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record InvitesBackend_Use(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] string InviteId
) : ISessionCommand<Invite>, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => ShardKey.New(InviteId);
}

/// <summary>
/// Command to generate a new invitation.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record InvitesBackend_Generate(
    [property: DataMember, Key(0)] Invite Invite
) : ICommand<Invite>, IBackendCommand, IHasShardKey
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => default;
}
