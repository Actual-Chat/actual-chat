using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat;

public class TracingCommandHandler : ICommandHandler<ICommand>
{
    protected IServiceProvider Services { get; }
    protected ILogger Log { get; }

    public TracingCommandHandler(
        IServiceProvider services,
        ILogger<TracingCommandHandler>? log = null)
    {
        Log = log ?? NullLogger<TracingCommandHandler>.Instance;
        Services = services;
    }

    [CommandHandler(Priority = 2_000_000_000, IsFilter = true)]
    public async Task OnCommand(ICommand command, CommandContext context, CancellationToken cancellationToken)
    {
        if (ShouldTrace(command, context)) {
            var commandType = command.GetType();
            var activityName = commandType.FullName ?? "UnknownCommand";
            using var activity = AppTrace.StartActivity(activityName);
            if (activity != null) {
                var tags = new ActivityTagsCollection { { "command", command.ToString() } };
                var activityEvent = new ActivityEvent($"command_event:{activityName}", tags: tags);
                activity.AddEvent(activityEvent);
            }
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
        }
        else {
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
        }
    }

    private bool ShouldTrace(ICommand command, CommandContext context)
    {
        // Do not trace meta commands
        if (command is IMetaCommand)
            return false;

        // Do not trace commands executed from completion in invalidating mode -
        // they should contain only invalidation logic and be extremely short.
        if (Computed.IsInvalidating())
            return false;

        return true;
    }
}
