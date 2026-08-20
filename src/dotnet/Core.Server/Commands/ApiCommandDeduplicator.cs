using System.Diagnostics;
using ActualChat.Diagnostics;
using ActualChat.Mesh;
using ActualChat.Serialization;
using ActualLab.CommandR.Internal;

namespace ActualChat.Commands;

/// <summary>
/// Server-side idempotency filter for <see cref="ApiCommand"/>: claims each outermost command by
/// <c>(SessionHash, Uuid)</c> in an <see cref="IIdempotencyStore"/>, runs it once, and replays the
/// stored result to duplicates. If the claim's owner node is dead (per <see cref="MeshWatcher"/>),
/// a duplicate reclaims the claim immediately instead of waiting out the TTL.
/// </summary>
public sealed class ApiCommandDeduplicator(IServiceProvider services) : ICommandHandler<ICommand>
{
    private const string LocalOwner = "local";
    // Only guards the live-but-slow case (a dead owner is reclaimed via liveness regardless of TTL);
    // must comfortably exceed the slowest realistic command, else a duplicate re-runs it.
    private static readonly TimeSpan InProgressTtl = TimeSpan.FromMinutes(5);
    // Dedup window: how long a completed result is replayed to duplicates. Covers client
    // retries/reconnects (seconds–minutes); revisit against prod result_size × command rate.
    private static readonly TimeSpan CompletedTtl = TimeSpan.FromHours(1);

    private static readonly KeyValuePair<string, object?> ExecutedTag = new("outcome", "executed");
    private static readonly KeyValuePair<string, object?> ReplayedTag = new("outcome", "replayed");
    private static readonly KeyValuePair<string, object?> WaitedTag = new("outcome", "waited");
    private static readonly KeyValuePair<string, object?> ClaimOp = new("op", "claim");
    private static readonly KeyValuePair<string, object?> CompleteOp = new("op", "complete");

    private IIdempotencyStore Store { get; } = services.GetRequiredService<IIdempotencyStore>();
    private MeshWatcher? MeshWatcher { get; } = services.GetService<MeshWatcher>();
    private ILogger Log { get; } = services.LogFor<ApiCommandDeduplicator>();

    [CommandFilter(Priority = CommanderCommandHandlerPriority.CommandTracer - 1_000_000)]
    public async Task OnCommand(ICommand command, CommandContext context, CancellationToken cancellationToken)
    {
        if (!context.IsOutermost || command is not ApiCommand { Uuid.Length: > 0 } apiCommand) {
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
            return;
        }

        var key = $"{apiCommand.Session.Hash}:{apiCommand.Uuid}";
        var owner = MeshWatcher?.ThisNode.Ref.Value ?? LocalOwner;
        var resultType = command.GetResultType();
        while (true) {
            var t0 = Stopwatch.GetTimestamp();
            var entry = await Store.ClaimOrGet(key, owner, InProgressTtl, cancellationToken).ConfigureAwait(false);
            IdempotencyMeters.StoreDuration.Record(Stopwatch.GetElapsedTime(t0).TotalMilliseconds, ClaimOp);
            switch (entry.State) {
            case IdempotencyState.Completed:
                context.SetResult(Deserialize(entry.Result, resultType));
                IdempotencyMeters.Outcome.Add(1, ReplayedTag);
                return;
            case IdempotencyState.New:
                await RunAndComplete(context, key, resultType, cancellationToken).ConfigureAwait(false);
                return;
            default:
                if (IsOwnerDead(entry.Owner)) {
                    var reclaimed = await Store
                        .TryReclaim(key, entry.Owner!, owner, InProgressTtl, cancellationToken)
                        .ConfigureAwait(false);
                    if (reclaimed is null)
                        continue; // State moved under us — re-claim.
                    if (reclaimed.State == IdempotencyState.New) {
                        IdempotencyMeters.Reclaim.Add(1);
                        Log.LogInformation("Dedup: owner {Owner} is dead, reclaimed {Key}", entry.Owner, key);
                        await RunAndComplete(context, key, resultType, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    context.SetResult(Deserialize(reclaimed.Result, resultType));
                    IdempotencyMeters.Outcome.Add(1, ReplayedTag);
                    return;
                }
                var result = await Store.WaitForResult(key, InProgressTtl, cancellationToken).ConfigureAwait(false);
                if (result is { } bytes) {
                    context.SetResult(Deserialize(bytes, resultType));
                    IdempotencyMeters.Outcome.Add(1, WaitedTag);
                    return;
                }
                IdempotencyMeters.Overrun.Add(1);
                Log.LogWarning(
                    "Dedup: marker for {Key} expired without a result, re-claiming (possible double run)", key);
                break; // Timed out — loop and re-evaluate (owner may have died meanwhile).
            }
        }
    }

    // Private methods

    private async Task RunAndComplete(
        CommandContext context, string key, Type resultType, CancellationToken cancellationToken)
    {
        try {
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) {
            // Failures aren't cached — drop the claim so a same-Uuid retry re-runs the command.
            await Store.Release(key, CancellationToken.None).ConfigureAwait(false);
            IdempotencyMeters.Release.Add(1);
            throw;
        }
        IdempotencyMeters.Outcome.Add(1, ExecutedTag);

        var resultBytes = Serialize(context.UntypedResult.Value, resultType);
        IdempotencyMeters.ResultSize.Record(resultBytes.Length);
        var t0 = Stopwatch.GetTimestamp();
        await Store.Complete(key, resultBytes, CompletedTtl, cancellationToken).ConfigureAwait(false);
        IdempotencyMeters.StoreDuration.Record(Stopwatch.GetElapsedTime(t0).TotalMilliseconds, CompleteOp);
    }

    private bool IsOwnerDead(string? owner)
    {
        // No mesh info (e.g. tests) — assume alive and fall back to the TTL/wait path.
        if (MeshWatcher is null || owner.IsNullOrEmpty())
            return false;

        var nodeRef = new NodeRef(owner, ParseOrNone.Option);
        if (nodeRef.IsNone)
            return false;

        return MeshWatcher.State.Value[nodeRef] is not { State: MeshNodeState.Online };
    }

    private static object? Deserialize(ReadOnlyMemory<byte> bytes, Type resultType)
        => Serializers.MessagePack.Read(bytes, resultType, out _);

    private static ReadOnlyMemory<byte> Serialize(object? value, Type resultType)
    {
        using var buffer = Serializers.MessagePack.Write(value, resultType);
        return buffer.WrittenSpan.ToArray();
    }
}
