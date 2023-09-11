using Npgsql;

namespace ActualChat.Testing.Host;

public class PostgreSqlPoolCleaner : IDisposable
{
    public void Dispose()
        // Every test method creates its own set of databases
        // Due to connections pooling, there can be idle connections left to databases
        // that we used for a test that is already executed.
        // https://github.com/npgsql/efcore.pg/issues/2417
        // These idle connections increase total connections number to a PostgreSQL instance
        // and this can lead to 'too many clients' error.
        // Solution:
        // When test Host is being stopped, container will call this dispose method.
        // This will force idle connections closing.
        => NpgsqlConnection.ClearAllPools();
}
