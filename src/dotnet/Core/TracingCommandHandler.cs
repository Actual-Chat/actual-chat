using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            var name = commandType.FullName;
            using var activity = Tracer.StartActivity(activityName: name);
            if (activity != null) {
                var tags = new ActivityTagsCollection();
                tags.Add("command", command.ToString());
                var activityEvent = Tracer.CreateEvent(eventName: "command_event:" + name , tags: tags);
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
        // do not trace meta commands.
        if (command is IMetaCommand)
            return false;
        // do not trace commands executed from completion in invalidating mode.
        // Normally they should contain only invalidation logic and be extremely short.
        if (Computed.IsInvalidating())
            return false;
        // do not trace fusion commands for cleanliness
        //if (command.GetType().FullName.StartsWith("Stl.Fusion"))
        //    return false;
        return true;
    }
}
