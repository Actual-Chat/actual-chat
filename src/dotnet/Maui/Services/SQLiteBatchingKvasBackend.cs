using System.Text;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualLab.IO;
using SQLite;

namespace ActualChat.Maui.Services;

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

    private readonly SimpleConcurrentPool<SQLiteConnection>? _connectionPool; // null if Initialize failed -> no-op
    private readonly Lock _suspendLock = new();
    private volatile bool _isSuspended;

    private IServiceProvider Services { get; }
    private ILogger Log => field ??= Services.LogFor(GetType());

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SQLiteBatchingKvasBackend))]
    public SQLiteBatchingKvasBackend(FilePath dbPath, string version, IServiceProvider services, byte[]? key = null)
    {
        Services = services;
        _connectionPool = Initialize(dbPath, version, key);
        if (_connectionPool is null)
            return;

        var hostInfo = services.HostInfo();
        if (hostInfo.AppKind is AppKind.Ios or AppKind.MacOS)
            MauiBackgroundState.IsBackground.Updated += OnIsBackgroundUpdated;
    }

    public ValueTask<byte[]?[]> GetMany(string[] keys, CancellationToken cancellationToken = default)
    {
        if (keys.Length == 0)
            return ValueTask.FromResult<byte[]?[]>([]);

        var result = new byte[]?[keys.Length];
        if (_connectionPool == null)
            return ValueTask.FromResult(result);

        Resume();
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
            var keyIndexes = new Dictionary<string, int>();
            for (var i = 0; i < keys.Length; i++)
                keyIndexes[keys[i]] = i;
            foreach (var dbItem in DbHelpers.FindMany(connection, keys))
                result[keyIndexes[dbItem.Key]] = dbItem.Value;
        }
        // Log.LogDebug("GetMany({KeyCount} keys) -> {Count} items", keys.Length, result.Count(x => x != null));
        return ValueTask.FromResult(result);

        static int FindIndex(string[] keys, string key) {
            for (var i = 0; i < keys.Length; i++)
                if (keys[i] == key)
                    return i;
            return -1;
        }
    }

    public ValueTask<(string Key, byte[] Value)[]> ListAllEntries(CancellationToken cancellationToken = default)
    {
        if (_connectionPool == null)
            return ValueTask.FromResult(Array.Empty<(string Key, byte[] Value)>());

        Resume();
        using var lease = _connectionPool.Rent();
        var connection = lease.Resource;
        var dbItems = DbHelpers.SelectAll(connection);
        var result = dbItems.Select(dbItem => (dbItem.Key, dbItem.Value)).ToArray();
        return ValueTask.FromResult(result);
    }

    public Task SetMany(List<(string Key, byte[]? Value)> updates, CancellationToken cancellationToken = default)
    {
        if (_connectionPool == null || updates.Count == 0)
            return Task.CompletedTask;

        Resume();
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

        Resume();
        using var lease = _connectionPool.Rent();
        var connection = lease.Resource;
        connection.DeleteAll<DbItem>();
        return Task.CompletedTask;
    }

    // Private methods

    private SimpleConcurrentPool<SQLiteConnection>? Initialize(FilePath dbPath, string version, byte[]? key)
    {
        try {
            return InitializeCore(dbPath, version, key);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to initialize SQLite database, deleting and retrying");
            try {
                DeleteDbFiles(dbPath);
                return InitializeCore(dbPath, version, key);
            }
            catch (Exception e2) {
                Log.LogError(e2, "Failed to initialize SQLite database after retry");
                return null;
            }
        }
    }

    private SimpleConcurrentPool<SQLiteConnection> InitializeCore(FilePath dbPath, string version, byte[]? key)
    {
        var connectionCount = HardwareInfo.ProcessorCount + 2;
        var connections = new SimpleConcurrentPool<SQLiteConnection>(
            () => DbHelpers.OpenConnection(dbPath, key),
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

    private static void DeleteDbFiles(FilePath dbPath)
    {
        var path = (string)dbPath;
        foreach (var suffix in new[] { "", "-wal", "-shm" }) {
            var file = path + suffix;
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    private void OnIsBackgroundUpdated(State state, StateEventKind stateEventKind)
    {
        if (!MauiBackgroundState.IsBackground.Value)
            return;

        // iOS requires apps to release all file locks in 5s after backgrounding
        var suspendDelay = TimeSpan.FromSeconds(3);
        _ = Task.Delay(suspendDelay, CancellationToken.None)
            .ContinueWith(_ => {
                if (MauiBackgroundState.IsBackground.Value)
                    Suspend();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Suspend()
    {
        if (_connectionPool == null || _isSuspended)
            return;

        lock (_suspendLock) {
            if (_isSuspended) return; // Double-check locking

            _isSuspended = true;
            try {
                // Checkpoint WAL to flush all pending writes to the main db file,
                // then close all idle pooled connections to release file locks.
                using var lease = _connectionPool.Rent();
                var connection = lease.Resource;
                connection.ExecuteScalar<string>("PRAGMA wal_checkpoint(TRUNCATE)");
                connection.Close();
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to checkpoint WAL during suspend");
            }
            _connectionPool.Drain(static c => {
                try { c.Close(); } catch { /* Intended */ }
            });
        }
    }

    private void Resume()
    {
        if (!_isSuspended) return;
        lock (_suspendLock)
            _isSuspended = false;
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
        public static string SelectAllSql = null!;
        public static string FindSql = null!;
        public static string FindManySql = null!;
        public static string DeleteSql = null!;
        public static string UpsertSql = null!;

        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DbHelpers))]
        public static SQLiteConnection OpenConnection(FilePath dbPath, byte[]? key = null)
        {
            // byte[] key uses raw hex format (PRAGMA key = "x'...'"), skipping PBKDF2
            var connectionString = new SQLiteConnectionString(dbPath, OpenFlags, storeDateTimeAsTicks: true, key: key);
            var connection = new SQLiteConnection(connectionString);
            if (_initializedTag == null) {
                using var _ = Lock.EnterScope();
                if (_initializedTag == null) {
                    Mapping = connection.GetMapping(typeof(DbItem));
                    SelectAllSql = $"select * from {Mapping.TableName}";
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

        public static IEnumerable<DbItem> SelectAll(SQLiteConnection connection)
        {
            var cmd = connection.CreateCommand(SelectAllSql);
            return cmd.ExecuteDeferredQuery<DbItem>(Mapping);
        }

        public static DbItem? Find(SQLiteConnection connection, string key)
        {
            var cmd = connection.CreateCommand(FindSql, key);
            return cmd.ExecuteDeferredQuery<DbItem>(Mapping).FirstOrDefault();
        }

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
