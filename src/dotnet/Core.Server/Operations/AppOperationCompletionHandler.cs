using ActualLab.CommandR.Operations;
using ActualLab.Fusion.Operations.Internal;

namespace ActualChat.Operations;

/// <summary>
/// Adds <see cref="IEveryHostOperationHandler"/> fan-out to Fusion's completion handler, which is
/// the one place that runs on every host a committed operation reaches.
/// </summary>
public sealed class AppOperationCompletionHandler(IServiceProvider services) : FusionOperationCompletionHandler(services)
{
    private IEveryHostOperationHandler[] EveryHostHandlers
        => field ??= Services.GetServices<IEveryHostOperationHandler>().ToArray();

    public override async Task OnOperationCompleted(Operation operation, CommandContext? commandContext)
    {
        await base.OnOperationCompleted(operation, commandContext).ConfigureAwait(false);
        var handlers = EveryHostHandlers;
        if (handlers.Length == 0)
            return;

        // A non-null CommandContext means this host is the one the operation ran on - an invariant
        // OperationCompletionNotifier asserts, so each host gets exactly one of the two cases
        var isOrigin = commandContext is not null;
        foreach (var handler in handlers) {
            try {
                await handler.OnOperationCompleted(operation, isOrigin, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception e) {
                // Swallowed on purpose: a failure here would otherwise unmark the log entry and have
                // the whole operation redelivered, and none of these effects is worth that
                Log.LogError(e, "{Handler} failed for operation #{Uuid} ({Command})",
                    handler.GetType().GetName(), operation.Uuid, operation.Command?.GetType().GetName());
            }
        }
    }
}
