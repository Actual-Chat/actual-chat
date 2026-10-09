using System.Diagnostics;
using ActualChat.Db.Module;
using ActualChat.Mesh;
using ActualChat.Sharding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace ActualChat.Mui;

public class MuiDbBackend(IServiceProvider services) : IMuiDbBackend
{
        private const int MaxTimeoutMs = 120_000;
    private const int MaxBinaryChars = 64;

    private MeshNode ThisNode => field ??= services.GetRequiredService<MeshNode>();

    public virtual async Task<MuiDatabaseInfo[]> GetDatabases(RandomShardRef shardRef, CancellationToken cancellationToken)
    {
        var result = new List<MuiDatabaseInfo>();
        foreach (var entry in GetEntries())
            result.Add(await Describe(entry, cancellationToken).ConfigureAwait(false));
        return result.ToArray();
    }

    public virtual async Task<MuiSqlResult> RunQuery(MuiSqlQuery query, CancellationToken cancellationToken)
    {
        var statement = MuiSqlGuard.GetSingleStatement(query.Sql);
        var entry = GetEntries().SingleOrDefault(x => x.Name == query.Database)
            ?? throw StandardError.NotFound($"Database '{query.Database}' is not available on this host.");
        if (entry.DbKind != DbKind.PostgreSql)
            throw StandardError.NotSupported($"Database '{entry.Name}' is not a PostgreSQL database.");

        var maxRows = Math.Clamp(query.MaxRows, 1, MuiSqlQuery.MaxRowsLimit);
        var timeoutMs = Math.Clamp(query.TimeoutMs, 1000, MaxTimeoutMs);
        var stopwatch = Stopwatch.StartNew();

        // The query never touches a DbContext or its connection. Npgsql keeps one pool per connection string,
        // and this string (application name + startup options) is used by nothing else, so these connections
        // form a pool of their own. Every connection in it is read-only from the first packet (the startup
        // option), and Npgsql resets the session (DISCARD ALL) before putting it back.
        var connectionString = new NpgsqlConnectionStringBuilder(entry.ConnectionString) {
            ApplicationName = "mui-readonly",
            Options = "-c default_transaction_read_only=on",
            MaxPoolSize = 4,
            MaxAutoPrepare = 0,
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try {
            await Execute(connection, transaction, "SET TRANSACTION READ ONLY", cancellationToken)
                .ConfigureAwait(false);
            await Execute(connection, transaction,
                $"SET LOCAL statement_timeout = {timeoutMs}",
                cancellationToken).ConfigureAwait(false);
            await RequireReadOnly(connection, transaction, cancellationToken).ConfigureAwait(false);
            // PREPARE accepts a single SELECT, INSERT, UPDATE, DELETE, MERGE or VALUES statement only:
            // DDL, SET, COPY, DO, CALL and transaction control are rejected by the server
            await Execute(connection, transaction, $"PREPARE mui_query AS {statement}", cancellationToken)
                .ConfigureAwait(false);
            await using var command = new NpgsqlCommand("EXECUTE mui_query", connection, transaction);
            command.CommandTimeout = timeoutMs / 1000 + 5;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var columns = new MuiSqlColumn[reader.FieldCount];
            for (var i = 0; i < columns.Length; i++)
                columns[i] = new(reader.GetName(i), reader.GetDataTypeName(i));
            var rows = new List<string?[]>();
            var isTruncated = false;
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) {
                if (rows.Count >= maxRows) {
                    isTruncated = true;
                    break;
                }
                var row = new string?[columns.Length];
                for (var i = 0; i < row.Length; i++)
                    row[i] = reader.IsDBNull(i) ? null : Format(reader.GetValue(i));
                rows.Add(row);
            }
            return new MuiSqlResult(columns, rows.ToArray(), isTruncated,
                (int)stopwatch.ElapsedMilliseconds, ThisNode.Ref.Value);
        }
        finally {
            await Rollback(transaction).ConfigureAwait(false);
            await Cleanup(connection).ConfigureAwait(false);
        }
    }

    // Private methods

    private DbContextEntry[] GetEntries()
        => services.GetServices<DbContextEntry>().OrderBy(x => x.Name).ToArray();

    private async Task<MuiDatabaseInfo> Describe(DbContextEntry entry, CancellationToken cancellationToken)
    {
        if (entry.DbKind != DbKind.PostgreSql)
            return new MuiDatabaseInfo(entry.Name, entry.DbKind.ToString(), [entry.Name], [], "");

        await using var db = await entry.CreateContext(services, cancellationToken).ConfigureAwait(false);
        var model = db.GetService<IDesignTimeModel>().Model;
        var tables = new SortedDictionary<string, SortedDictionary<string, MuiColumnInfo>>();
        foreach (var entityType in model.GetEntityTypes()) {
            var tableName = entityType.GetTableName();
            if (tableName is null)
                continue;

            var schema = entityType.GetSchema();
            var tableId = StoreObjectIdentifier.Table(tableName, schema);
            var fullName = schema.IsNullOrEmpty() ? tableName : $"{schema}.{tableName}";
            if (!tables.TryGetValue(fullName, out var columns))
                tables[fullName] = columns = new();
            foreach (var property in entityType.GetProperties()) {
                var columnName = property.GetColumnName(tableId);
                if (columnName is null)
                    continue;

                columns[columnName] = new MuiColumnInfo(
                    columnName,
                    property.GetColumnType(tableId),
                    property.IsColumnNullable(tableId),
                    property.IsPrimaryKey());
            }
        }
        var tableInfos = tables
            .Select(x => new MuiTableInfo(x.Key, x.Value.Values.ToArray()))
            .ToArray();
        return new MuiDatabaseInfo(
            entry.Name,
            entry.DbKind.ToString(),
            [entry.Name],
            tableInfos,
            db.Database.GenerateCreateScript());
    }

    private static async Task Execute(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RequireReadOnly(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT current_setting('default_transaction_read_only'), current_setting('transaction_read_only')",
            connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || reader.GetString(0) != "on"
            || reader.GetString(1) != "on")
            throw StandardError.Internal("The connection is not read-only.");
    }

    // A prepared statement outlives a rolled back transaction; Npgsql's reset on return clears it too,
    // this just doesn't rely on it (and leaves Npgsql's own statements alone)
    private static async Task Cleanup(NpgsqlConnection connection)
    {
        try {
            if (connection.State == System.Data.ConnectionState.Open) {
                await using var command = new NpgsqlCommand("DEALLOCATE mui_query", connection);
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
        catch {
            // A broken connection is discarded by the pool
        }
    }

    private static async Task Rollback(NpgsqlTransaction transaction)
    {
        try {
            await transaction.RollbackAsync().ConfigureAwait(false);
        }
        catch {
            // The connection is closed right after, which rolls the transaction back anyway
        }
    }

    private static string Format(object value)
        => value switch {
            string s => s,
            DateTime v => v.ToString("O"),
            DateTimeOffset v => v.ToString("O"),
            bool v => v ? "true" : "false",
            byte[] v => "\\x" + Convert.ToHexString(v, 0, Math.Min(v.Length, MaxBinaryChars / 2))
                + (v.Length > MaxBinaryChars / 2 ? "..." : ""),
            Array v => "{" + string.Join(",", v.Cast<object?>().Select(x => x is null ? "NULL" : Format(x))) + "}",
            _ => value.ToString() ?? "",
        };
}
