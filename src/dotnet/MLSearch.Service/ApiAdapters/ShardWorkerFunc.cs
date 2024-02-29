namespace ActualChat.MLSearch.ApiAdapters;

internal sealed class StreamShardScheme(string name, int shardCount) :
    ShardScheme(name, shardCount);


internal class ShardWorkerFunc(
    string name,
    int shardCount,
    IServiceProvider services,
    Func<int, CancellationToken, Task> run
) :
    ShardWorker(services, new StreamShardScheme(name, shardCount))
{
    protected override Task OnRun(int shardIndex, CancellationToken cancellationToken)
        => run(shardIndex, cancellationToken);
}
