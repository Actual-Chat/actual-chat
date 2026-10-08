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
    // Null when the user is free and no call of theirs ended lately. A claim the chat's call no longer
    // backs reads as Ended rather than as nothing. The phase is the call's, not the stored one.
    [ComputeMethod]
    Task<UserCall?> GetUserCall(UserId userId, CancellationToken cancellationToken);

    // Reports whether the user's call is this one now: true when it was free, held by this call
    // already, or held by a claim the chat's call no longer backs.
    Task<bool> TryClaim(UserId userId, UserCall call, CancellationToken cancellationToken);
    // Turns the claim Ended only while it is still this call's: an end that arrives late must not
    // free a user who is in the next call to the same chat by then.
    Task EndCall(UserId userId, CallId callId, CallOutcome outcome, CancellationToken cancellationToken);
}
