using ActualChat.Flows;
using ActualChat.Queues;

namespace ActualChat.Testing.Host;

public static class FlowsTestExt
{
    public static async Task WhenFlowsStarted(
        this IServiceProvider services,
        TimeSpan? timeout = null,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
    {
        // A resume goes through a queue shard and then a flow backend shard, and a fresh host
        // takes a while to start both
        await services.ShardOwner<IFlowBackend>()
            .WhenOwned(timeout, callerFilePath, callerLine)
            .ConfigureAwait(false);
        await services.Queues()
            .WhenStarted(timeout, callerFilePath, callerLine)
            .ConfigureAwait(false);
    }
}
