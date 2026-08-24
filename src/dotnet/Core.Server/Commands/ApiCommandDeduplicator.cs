using ActualChat.Diagnostics;
using ActualChat.Serialization;
using ActualLab.CommandR.Internal;

namespace ActualChat.Commands;

/// <summary>
/// Server-side idempotency filter for <see cref="ApiCommand"/>: claims each outermost command by
/// <c>(SessionHash, Uuid)</c> in the <see cref="IdempotencyStore"/>, runs it once, and replays the
/// stored result to duplicates that reach the same node.
/// </summary>
public sealed class ApiCommandDeduplicator(IServiceProvider services) : ICommandHandler<ICommand>
{
    private static readonly KeyValuePair<string, object?> ExecutedTag = new("outcome", "executed");
    private static readonly KeyValuePair<string, object?> ReplayedTag = new("outcome", "replayed");
    private static readonly KeyValuePair<string, object?> WaitedTag = new("outcome", "waited");

    private IdempotencyStore Store { get; } = services.GetRequiredService<IdempotencyStore>();
    private ILogger Log { get; } = services.LogFor<ApiCommandDeduplicator>();

    [CommandFilter(Priority = CommanderCommandHandlerPriority.CommandTracer - 1_000_000)]
    public async Task OnCommand(ICommand command, CommandContext context, CancellationToken cancellationToken)
    {
        if (!context.IsOutermost
            || command is INotDeduplicated
            || command is not ApiCommand { Uuid.Length: > 0 } apiCommand) {
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
            return;
        }

        var key = $"{apiCommand.Session.Hash}:{apiCommand.Uuid}";
        var resultType = command.GetResultType();
        while (true) {
            if (Store.TryClaim(key, out var entry)) {
                await RunAndComplete(context, entry, resultType, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (entry.Result is { } result) {
                context.SetResult(Deserialize(result, resultType));
                IdempotencyMeters.Outcome.Add(1, ReplayedTag);
                return;
            }

            var awaitedResult = await WaitForResult(entry, key, cancellationToken).ConfigureAwait(false);
            if (awaitedResult is { } bytes) {
                context.SetResult(Deserialize(bytes, resultType));
                IdempotencyMeters.Outcome.Add(1, WaitedTag);
                return;
            }

            // The claim was dropped without a result (its owner failed or overran) — loop and re-claim.
        }
    }

    // Private methods

    private static async Task RunAndComplete(
        CommandContext context, IdempotencyEntry entry, Type resultType, CancellationToken cancellationToken)
    {
        try {
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) {
            // Failures aren't cached — drop the claim so a same-Uuid retry re-runs the command.
            entry.Release();
            IdempotencyMeters.Release.Add(1);
            throw;
        }

        IdempotencyMeters.Outcome.Add(1, ExecutedTag);
        var resultBytes = Serialize(context.UntypedResult.Value, resultType);
        IdempotencyMeters.ResultSize.Record(resultBytes.Length);
        entry.Complete(resultBytes);
    }

    private async Task<ReadOnlyMemory<byte>?> WaitForResult(
        IdempotencyEntry entry, string key, CancellationToken cancellationToken)
    {
        try {
            return await entry.WhenCompleted
                .WaitAsync(Store.InProgressTtl, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            IdempotencyMeters.Overrun.Add(1);
            Log.LogWarning("Dedup: claim for {Key} outlived its TTL without a result (possible double run)", key);
            entry.Release();
            return null;
        }
    }

    private static object? Deserialize(ReadOnlyMemory<byte> bytes, Type resultType)
        => Serializers.MessagePack.Read(bytes, resultType, out _);

    private static ReadOnlyMemory<byte> Serialize(object? value, Type resultType)
    {
        using var buffer = Serializers.MessagePack.Write(value, resultType);
        return buffer.WrittenSpan.ToArray();
    }
}
