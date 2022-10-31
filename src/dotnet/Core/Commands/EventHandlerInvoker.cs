namespace ActualChat.Commands;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class EventHandlerInvoker : ICommandHandler<IEvent>
{
    [CommandHandler(IsFilter = true, Priority = 0.001)]
    public virtual async Task OnCommand(IEvent command, CommandContext context, CancellationToken cancellationToken)
    {
        if (context.ExecutionState.IsFinal) // No event handlers for this event
            return;
        if (Computed.IsInvalidating()) // This should never happen!
            return;

        var eventHandlers = context.ExecutionState.Handlers
            .Skip(context.ExecutionState.NextHandlerIndex)
            .ToList();
        if (eventHandlers.Any(h => h.IsFilter))
            throw Errors.EventHandlerHubShouldBeTheLastFilter(command.GetType());

        // Invoking all event handlers in parallel
        await eventHandlers
            .Select(h => h.Invoke(command, context, cancellationToken))
            .Collect(0)
            .ConfigureAwait(false);
    }
}
