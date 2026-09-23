using ActualChat.Attributes;
using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming;

/// <summary>
/// The one call each user is in, across every device they're signed in on. Keyed by
/// <see cref="UserId"/>, so it lands on the user's shard rather than the chat's.
/// </summary>
[BackendService(nameof(HostRole.LiveBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.LiveBackend))]
public interface ICallsBackend : IComputeService, IBackendService
{
    // Null when the user is free, or when their claim no longer matches the chat's live session -
    // a stale claim is released rather than reported. The phase is the session's, not the stored one.
    [ComputeMethod]
    Task<UserCall?> GetUserCall(UserId userId, CancellationToken cancellationToken);

    // Reports whether the user's call is this one now: true when it was free, held by this chat
    // already, or held by a claim the chat's session no longer backs.
    Task<bool> TryClaim(UserId userId, UserCall call, CancellationToken cancellationToken);
    Task Release(UserId userId, ChatId chatId, CancellationToken cancellationToken);
}
