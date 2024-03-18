
namespace ActualChat.MLSearch.ApiAdapters;

// Note:
// This is super confusing. The scheme name MUST be a role name.
// It is getting a scheme from the role of the host. Super-super confusing.
// And the immediate next problem: Shard worker is expecting to accept a sharding scheme.
// This makes me think that I can create different schemes for different shard workers.
// However since it applies on the role level it's not a correct expectation.
// In order to solve this. I would suggest to:
// - Very Explicitly register a shard scheme against a role.
// - modify how this workers are getting registered:
//   services.AddWorker<HostRole, TWorker / TShardWorker>
// -

internal class ShardWorkerFunc<TName>(
    IServiceProvider services,
    ShardScheme shardScheme,
    Func<int, CancellationToken, Task> run
): ShardWorkerFunc(services, shardScheme, typeof(TName).Name, run);

internal class ShardWorkerFunc(
    IServiceProvider services,
    ShardScheme shardScheme,
    string shardKeySuffix,
    Func<int, CancellationToken, Task> run
) :
    ShardWorker(services, shardScheme, shardKeySuffix)
{
    protected override Task OnRun(int shardIndex, CancellationToken cancellationToken)
        => run(shardIndex, cancellationToken);
}
