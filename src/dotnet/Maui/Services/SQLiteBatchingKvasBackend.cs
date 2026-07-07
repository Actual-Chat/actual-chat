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

    public SQLiteBatchingKvasBackend(FilePath dbPath, string version, IServiceProvider services, byte[]? key = null)
    {
        Services = services;
        _connectionPool = Initialize(dbPath, version, key);
        if (_connectionPool is null)
            return;

        var hostInfo = services.HostInfo();
        if (hostInfo.AppKind is AppKind.Ios or AppKind.MacOS)
            MauiBackgroundState.RegisterStateHandler(isBackground => {
                if (isBackground)
                    Suspend();
                else
                    Resume();
            });
    }

    public ValueTask<byte[]?[]> GetMany(string[] keys, CancellationToken cancellationToken = default)
    {
        if (keys.Length == 0)
            return ValueTask.FromResult<byte[]?[]>([]);

        var result = new byte[]?[keys.Length];
        if (_connectionPool == null)
            return ValueTask.FromResult(result);

        Run(connection => {
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
        });
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

        var result = Run(connection => DbHelpers.SelectAll(connection)
            .Select(dbItem => (dbItem.Key, dbItem.Value))
            .ToArray());
        return ValueTask.FromResult(result);
    }

    public Task SetMany(List<(string Key, byte[]? Value)> updates, CancellationToken cancellationToken = default)
    {
        if (_connectionPool == null || updates.Count == 0)
            return Task.CompletedTask;

        Run(connection => {
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
        });
        return Task.CompletedTask;
    }

    public Task Clear(CancellationToken cancellationToken = default)
    {
        if (_connectionPool == null)
            return Task.CompletedTask;

        Run(connection => connection.DeleteAll<DbItem>());
        return Task.CompletedTask;
    }

    // Private methods

    private SimpleConcurrentPool<SQLiteConnection>? Initialize(FilePath dbPath, string version, byte[]? key)
    {
        try {
            return InitializeCore(dbPath, version, key);
        }
        catch (Exception e) {
            // InitializeCore drains its own connection pool on failure before rethrowing, so
            // the file handles are already released by the time we reach DeleteDbFiles.
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

    private static SimpleConcurrentPool<SQLiteConnection> InitializeCore(FilePath dbPath, string version, byte[]? key)
    {
        var connectionCount = HardwareInfo.ProcessorCount + 2;
        var connections = new SimpleConcurrentPool<SQLiteConnection>(
            () => DbHelpers.OpenConnection(dbPath, key),
            static c => !c.IsInTransaction,
            connectionCount);

        try {
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
        catch {
            // Release file locks before propagating so the caller can delete the db file.
            connections.Drain(static c => {
                try { c.Close(); } catch { /* Intended */ }
            });
            throw;
        }
    }

    private static void DeleteDbFiles(FilePath dbPath)
    {
        // Force finalizers so any SQLite handles released by Close() but not yet by the
        // managed GC are fully cleaned up before we try to delete the backing files.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var path = (string)dbPath;
        foreach (var suffix in new[] { "", "-wal", "-shm" }) {
            var file = path + suffix;
            if (!File.Exists(file))
                continue;

            DeleteWithRetry(file);
        }
    }

    private static void DeleteWithRetry(string file)
    {
        // Windows can briefly hold the file open after sqlite3_close returns — retry on
        // ERROR_SHARING_VIOLATION for up to ~500ms, then swallow: the init path is a
        // best-effort recovery and a failed delete just falls through to the outer catch.
        const int maxAttempts = 5;
        var delayMs = 20;
        for (var attempt = 1; ; attempt++) {
            try {
                File.Delete(file);
                return;
            }
            catch (IOException) when (attempt < maxAttempts) {
                Thread.Sleep(delayMs);
                delayMs *= 2;
            }
        }
    }

    // Runs an operation on a pooled connection. While suspended (backgrounded) the connection is
    // checkpointed and closed afterwards instead of being returned open to the pool — an open
    // connection left holding a WAL/-shm file lock across suspension is what iOS kills with 0xdead10cc.
    private void Run(Action<SQLiteConnection> action)
        => Run(connection => {
            action(connection);
            return true;
        });

    private T Run<T>(Func<SQLiteConnection, T> func)
    {
        var lease = _connectionPool!.Rent();
        var connection = lease.Resource;
        T result;
        try {
            result = func(connection);
        }
        catch {
            // Don't return a possibly-broken connection to the pool.
            CloseQuietly(connection);
            throw;
        }
        // The suspend check and the re-pool/close decision must be atomic w.r.t. Suspend's drain,
        // otherwise a connection could be re-pooled open right after Suspend has drained the pool.
        lock (_suspendLock) {
            if (_isSuspended) {
                TryCheckpoint(connection);
                CloseQuietly(connection);
            }
            else
                lease.Dispose();
        }
        return result;
    }

    private void Suspend()
    {
        if (_connectionPool == null || _isSuspended)
            return;

        lock (_suspendLock) {
            if (_isSuspended)
                return;

            _isSuspended = true;
            // Checkpoint via a rented connection, then close it (dropped, not re-pooled), and close
            // every idle connection so no WAL/-shm file lock survives into suspension.
            var lease = _connectionPool.Rent();
            TryCheckpoint(lease.Resource);
            CloseQuietly(lease.Resource);
            _connectionPool.Drain(CloseQuietly);
        }
    }

    private void Resume()
        // Going foreground: pooling open connections is fine again. Lock-free so the main-thread
        // OnActivated -> Set(false) callback never stalls behind a background op's checkpoint.
        => _isSuspended = false;

    private void TryCheckpoint(SQLiteConnection connection)
    {
        try {
            connection.ExecuteScalar<string>("PRAGMA wal_checkpoint(TRUNCATE)");
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to checkpoint WAL");
        }
    }

    private static void CloseQuietly(SQLiteConnection connection)
    {
        try { connection.Close(); } catch { /* Intended */ }
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
