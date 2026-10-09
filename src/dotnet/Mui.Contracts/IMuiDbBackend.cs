using ActualChat.Attributes;
using ActualChat.Sharding;
using ActualLab.Rpc;

namespace ActualChat.Mui;

// Runs on a backend host that has access to the databases; the first parameter decides which one:
// the schema comes from a random one, a query from the one its SQL text hashes to
[BackendService(nameof(HostRole.DiagnosticsBackend), ServiceMode.Distributed)]
[BackendShardScheme(nameof(HostRole.DiagnosticsBackend))]
public interface IMuiDbBackend : IRpcService, IBackendService
{
    Task<MuiDatabaseInfo[]> GetDatabases(RandomShardRef shardRef, CancellationToken cancellationToken);
    Task<MuiSqlResult> RunQuery(MuiSqlQuery query, CancellationToken cancellationToken);
}
