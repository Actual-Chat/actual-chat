namespace ActualChat.Flows.Infrastructure;

public sealed class FlowEventForwarder : ICommandHandler<IFlowEvent>
{
    private IFlows Flows { get; }
    private ICommander Commander { get; }
    private ILogger Log { get; }

    public FlowEventForwarder(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        Flows = services.GetRequiredService<IFlows>();
        Commander = services.Commander();
    }

    [CommandHandler(IsFilter = false)]
    public Task OnCommand(IFlowEvent command, CommandContext context, CancellationToken cancellationToken)
        => Flows.OnEvent(command.FlowId, command, cancellationToken);
}
