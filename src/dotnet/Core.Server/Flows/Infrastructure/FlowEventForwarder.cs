namespace ActualChat.Flows.Infrastructure;

public sealed class FlowEventForwarder(IServiceProvider services) : ICommandHandler<IFlowEvent>
{
    private IFlows Flows { get; } = services.GetRequiredService<IFlows>();

    [CommandHandler]
    public Task OnCommand(IFlowEvent command, CommandContext context, CancellationToken cancellationToken)
        => Flows.OnEvent(command.FlowId, command, cancellationToken);
}
