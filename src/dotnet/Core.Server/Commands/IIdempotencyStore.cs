namespace ActualChat.Commands;

/// <summary>
/// Distributed idempotency primitive: atomically claims a key for execution (recording the owner),
/// replays a stored result to duplicates, lets waiters on other nodes block for that result, and
/// allows reclaiming a claim whose owner died.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyEntry> ClaimOrGet(string key, string owner, TimeSpan ttl, CancellationToken cancellationToken);
    Task Complete(string key, ReadOnlyMemory<byte> resultMessage, TimeSpan ttl, CancellationToken cancellationToken);
    Task<ReadOnlyMemory<byte>?> WaitForResult(string key, TimeSpan timeout, CancellationToken cancellationToken);
    Task<IdempotencyEntry?> TryReclaim(
        string key, string expectedOwner, string newOwner, TimeSpan ttl, CancellationToken cancellationToken);
    Task Release(string key, CancellationToken cancellationToken);
}

public enum IdempotencyState { New, InProgress, Completed }

/// <summary>
/// Outcome of a claim/steal: <see cref="State"/> tells whether the caller owns execution
/// (<see cref="IdempotencyState.New"/>), must wait, or can replay <see cref="Result"/>.
/// <see cref="Owner"/> identifies the current in-progress owner (for liveness checks).
/// </summary>
public sealed record IdempotencyEntry(
    IdempotencyState State,
    ReadOnlyMemory<byte> Result = default,
    string? Owner = null);
