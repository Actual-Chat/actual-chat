namespace ActualChat.Flows.Infrastructure;

public sealed class FlowEventForwarder(IServiceProvider services) : ICommandHandler<IFlowEvent>
{
    private IFlows Flows { get; } = services.GetRequiredService<IFlows>();
    private MomentClockSet Clocks { get; } = services.Clocks();

    [CommandHandler]
    public Task OnCommand(IFlowEvent command, CommandContext context, CancellationToken cancellationToken)
    {
        if (command is IDelayed delayed) {
            var delay = delayed.DelayUntil - Clocks.SystemClock.Now;
            if (delay > TimeSpan.Zero)
                throw StandardError.Postpone(delay.Value);
        }
        return Flows.OnEvent(command.FlowId, command, cancellationToken);
    }
}
