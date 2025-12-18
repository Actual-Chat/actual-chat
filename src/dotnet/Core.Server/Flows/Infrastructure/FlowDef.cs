using ActualChat.Queues;

namespace ActualChat.Flows.Infrastructure;

public sealed record FlowDef(Type Type, Symbol Name)
{
    public int DataVersion { get; init; } = 1;
    public TimeSpan ResumeTimeout { get; init; } = QueuesExt.DefaultTimeout;
}
