namespace ActualChat.Chat;

/// <summary>
/// Service for resolving human-friendly aliases to chats and places.
/// </summary>
public interface IAliases : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    Task<AliasTarget?> GetTarget(AliasId aliasId, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    Task<PlaceChatId?> GetPlaceChatIdByAlias(PlaceId placeId, AliasId aliasId, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    Task<UserId?> GetUserIdByAlias(AliasId aliasId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Specifies the type of entity an alias points to.
/// </summary>
public enum AliasKind { Chat, Place }

/// <summary>
/// Represents the target of an alias resolution.
/// </summary>
[DataContract, MessagePackObject]
public partial record AliasTarget(
    [property: DataMember, Key(0)] AliasKind Kind,
    [property: DataMember, Key(1)] string TargetId);
