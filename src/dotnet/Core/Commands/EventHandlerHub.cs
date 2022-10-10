namespace ActualChat.Commands;

public class EventHandlerHub : ICommandHandler<IEvent>
{
    [CommandHandler(Priority = 1, IsFilter = true)]
    public virtual async Task OnCommand(IEvent command, CommandContext context, CancellationToken cancellationToken)
    {
        if (context.ExecutionState.IsFinal)
            return;

        if (Computed.IsInvalidating()) {
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
            return;
        }

        var remainingHandlers = context.ExecutionState.Handlers
            .Skip(context.ExecutionState.NextHandlerIndex)
            .ToList();

        if (remainingHandlers.Any(h => h.IsFilter))
            throw Errors.EventHandlerHubShouldBeTheLastFilter(command.GetType());

        var handlerTasks = remainingHandlers
            .Select(async handler => await handler
                .Invoke(command, context, cancellationToken).ConfigureAwait(false))
            .ToList();
        try {
            var whenAllCompleted = Task.WhenAll(handlerTasks);
            await whenAllCompleted.ConfigureAwait(false);
        }
        catch (Exception ex) {
            context.SetResult(ex);
            throw;
        }
    }
}
