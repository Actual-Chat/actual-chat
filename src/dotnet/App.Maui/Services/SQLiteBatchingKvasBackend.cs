using System.Text;
using ActualChat.Kvas;
using SQLite;
using ActualLab.IO;

namespace ActualChat.App.Maui.Services;

// ReSharper disable once InconsistentNaming
public sealed class SQLiteBatchingKvasBackend : IBatchingKvasBackend
{
    public const string VersionKey = "(version)";
    private const SQLiteOpenFlags OpenFlags =
        // Open the database in read/write mode
        SQLiteOpenFlags.ReadWrite |
        // Create the database if it doesn't exist
        SQLiteOpenFlags.Create |
        // Assume each connection is never used concurrently
        SQLiteOpenFlags.NoMutex;

    private readonly SimpleConcurrentPool<SQLiteConnection>? _connectionPool;

    private IServiceProvider Services { get; }
    [field: MaybeNull, AllowNull]
    private ILogger Log => field ??= Services.LogFor(GetType());

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SQLiteBatchingKvasBackend))]
    public SQLiteBatchingKvasBackend(FilePath dbPath, string version, IServiceProvider services)
    {
        Services = services;
        _connectionPool = Initialize(dbPath, version);
    }

    public ValueTask<byte[]?[]> GetMany(string[] keys, CancellationToken cancellationToken = default)
    {
        if (keys.Length == 0)
            return ValueTask.FromResult<byte[]?[]>([]);

        var result = new byte[]?[keys.Length];
        if (_connectionPool == null)
            return ValueTask.FromResult(result);

        using var lease = _connectionPool.Rent();
        var connection = lease.Resource;
        if (keys.Length == 1)
            result[0] = DbHelpers.Find(connection, keys[0])?.Value;
        else if (keys.Length < 16) {
            // Small number of keys, use a simple loop
            foreach (var dbItem in DbHelpers.FindMany(connection, keys))
                result[FindIndex(keys, dbItem.Key)] = dbItem.Value;
        }
        else {
            // Large number of keys, use a dictionary
            var keyIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < keys.Length; i++)
                keyIndexes[keys[i]] = i;
            foreach (var dbItem in DbHelpers.FindMany(connection, keys))
                result[keyIndexes[dbItem.Key]] = dbItem.Value;
        }
        // Log.LogDebug("GetMany({KeyCount} keys) -> {Count} items", keys.Length, result.Count(x => x != null));
        return ValueTask.FromResult(result);

        static int FindIndex(string[] keys, string key) {
            for (var i = 0; i < keys.Length; i++)
                if (OrdinalEquals(keys[i], key))
                    return i;
            return -1;
        }
    }

    public Task SetMany(List<(string Key, byte[]? Value)> updates, CancellationToken cancellationToken = default)
    {
        if (_connectionPool == null || updates.Count == 0)
            return Task.CompletedTask;

        using var lease = _connectionPool.Rent();
        var connection = lease.Resource;
        if (updates.Count == 1)
            DbHelpers.Set(connection, updates[0]);
        else {
            var savepoint = connection.SaveTransactionPoint();
            try {
                foreach (var update in updates)
                    DbHelpers.Set(connection, update);
                connection.Release(savepoint);
            }
            catch {
                connection.Rollback();
                throw;
            }
        }
        return Task.CompletedTask;
    }

    public Task Clear(CancellationToken cancellationToken = default)
    {
        if (_connectionPool == null)
            return Task.CompletedTask;

        using var lease = _connectionPool.Rent();
        var connection = lease.Resource;
        connection.DeleteAll<DbItem>();
        return Task.CompletedTask;
    }

    // Private methods

    private SimpleConcurrentPool<SQLiteConnection>? Initialize(FilePath dbPath, string version)
    {
        try {
            var connectionCount = HardwareInfo.ProcessorCount + 2;
            var connections = new SimpleConcurrentPool<SQLiteConnection>(
                () => DbHelpers.OpenConnection(dbPath),
                static c => !c.IsInTransaction,
                connectionCount);

            using var lease = connections.Rent();
            var connection = lease.Resource;
            // connection.EnableWriteAheadLogging();
            connection.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
            connection.ExecuteScalar<string>("PRAGMA synchronous=normal");
            connection.ExecuteScalar<string>("PRAGMA journal_size_limit=2048000");
            var versionBytes = Encoding.UTF8.GetEncoder().Convert(version);
            if (connection.CreateTable<DbItem>() == CreateTableResult.Migrated) {
                var existingVersionBytes = connection.Find<DbItem>(VersionKey)?.Value ?? [];
                if (!versionBytes.AsSpan().SequenceEqual(existingVersionBytes.AsSpan())) {
                    _ = connection.DropTable<DbItem>();
                    _ = connection.CreateTable<DbItem>();
                }
            }
            connection.InsertOrReplace(new DbItem { Key = VersionKey, Value = versionBytes });
            return connections;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize SQLite database");
            return null;
        }
    }

    // Nested types

    [Table("items")]
    public sealed class DbItem
    {
        [PrimaryKey] public string Key { get; set; } = "";
        public byte[] Value { get; set; } = null!;
    }

    public static class DbHelpers
    {
        private static readonly Lock Lock = new();
        private static volatile object? _initializedTag;

        public static TableMapping Mapping = null!;
        public static string FindSql = null!;
        public static string FindManySql = null!;
        public static string DeleteSql = null!;
        public static string UpsertSql = null!;

        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DbHelpers))]
        public static SQLiteConnection OpenConnection(FilePath dbPath)
        {
            var connection = new SQLiteConnection(dbPath, OpenFlags);
            if (_initializedTag == null) {
                using var _ = Lock.EnterScope();
                if (_initializedTag == null) {
                    Mapping = connection.GetMapping(typeof(DbItem));
                    FindSql = Mapping.GetByPrimaryKeySql;
                    FindManySql =
                        $"select * from {Mapping.TableName} where Key in (select e.value from json_each(?) e)";
                    DeleteSql = $"delete from {Mapping.TableName} where Key = ?";
                    UpsertSql = $"insert or replace into {Mapping.TableName} (Key, Value) values (?, ?)";
                    _initializedTag = new();
                }
            }
            return connection;
        }

        public static DbItem? Find(SQLiteConnection connection, string key)
        {
            var cmd = connection.CreateCommand(FindSql, key);
            return cmd.ExecuteDeferredQuery<DbItem>(Mapping).FirstOrDefault();
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "string[] is always JSON-serializable")]
        public static IEnumerable<DbItem> FindMany(SQLiteConnection connection, string[] keys)
        {
            var keysJson = JsonSerializer.Serialize(keys);
            var cmd = connection.CreateCommand(FindManySql, keysJson);
            return cmd.ExecuteDeferredQuery<DbItem>(Mapping);
        }

        public static void Set(SQLiteConnection connection, (string Key, byte[]? Value) item)
        {
            if (item.Value == null)
                connection.Execute(DeleteSql, item.Key);
            else
                connection.Execute(UpsertSql, item.Key, item.Value);
        }
    }
}
