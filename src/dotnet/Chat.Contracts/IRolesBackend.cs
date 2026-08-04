using ActualLab.Rpc;

namespace ActualChat.Chat;

/// <summary>
/// Backend service for managing chat roles and permissions.
/// </summary>
public interface IRolesBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Role?> Get(ChatId chatId, RoleId roleId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Role[]> List(
        ChatId chatId, AuthorId authorId,
        bool isGuest, bool isAnonymous,
        CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Role[]> ListSystem(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<AuthorId[]> ListAuthorIds(ChatId chatId, RoleId roleId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Role> Change(RolesBackend_Change command, CancellationToken cancellationToken);
}

/// <summary>
/// Command to create, update, or delete a role.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record RolesBackend_Change(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] RoleId? RoleId,
    [property: DataMember, Key(2)] long? ExpectedVersion,
    [property: DataMember, Key(3)] Change<RoleDiff> Change
) : ICommand<Role>, IBackendCommand, IHasShardKey<ChatId>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => ChatId;
}
