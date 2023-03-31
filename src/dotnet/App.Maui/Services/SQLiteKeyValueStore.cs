using Microsoft.Extensions.ObjectPool;
using SQLite;
using Stl.IO;

namespace ActualChat.App.Maui.Services;

// ReSharper disable once InconsistentNaming
public class SQLiteKeyValueStore : FlushingKeyValueStore
{
    private const SQLiteOpenFlags DbOpenFlags =
        // Open the database in read/write mode
        SQLiteOpenFlags.ReadWrite |
        // Create the database if it doesn't exist
        SQLiteOpenFlags.Create |
        // Assume each connection is never used concurrently
        SQLiteOpenFlags.NoMutex;

    private readonly ObjectPool<SQLiteConnection>? _connections;
    private readonly SemaphoreSlim _semaphore;

    public FilePath DbPath { get; }

    public SQLiteKeyValueStore(FilePath dbPath, IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        DbPath = dbPath;
        _semaphore = new SemaphoreSlim(HardwareInfo.ProcessorCount);

        try {
            var connectionsPolicy = new SQLiteConnectionPoolPolicy(DbPath, DbOpenFlags);
            _connections = new DefaultObjectPool<SQLiteConnection>(connectionsPolicy, HardwareInfo.ProcessorCount + 2);
            var connection = _connections.Get();
            try {
                connection.EnableWriteAheadLogging();
                _ = connection.CreateTable<DbItem>();
            }
            finally {
                _connections.Return(connection);
            }
        }
        catch (Exception e) {
            _connections = null;
            Log.LogError(e, "Failed to initialize SQLite database");
        }
    }

    protected override async ValueTask<string?> StorageGet(HashedString key, CancellationToken cancellationToken)
    {
        if (_connections == null)
            return null;

        var connection = await AcquireConnection(cancellationToken).ConfigureAwait(false);
        try {
            var item = connection.Find<DbItem?>(key.Value);
            return item?.Value;
        }
        finally {
            ReleaseConnection(connection);
        }
    }

    protected override async ValueTask StorageSet(HashedString key, string? value, CancellationToken cancellationToken)
    {
        if (_connections == null)
            return;

        var connection = await AcquireConnection(cancellationToken).ConfigureAwait(false);
        try {
            if (value == null)
                connection.Delete<DbItem>(key.Value);
            else {
                var item = new DbItem { Key = key.Value, Value = value };
                connection.InsertOrReplace(item);
            }
        }
        finally {
            ReleaseConnection(connection);
        }
    }

    protected override async ValueTask StorageClear()
    {
        if (_connections == null)
            return;

        var connection = await AcquireConnection().ConfigureAwait(false);
        try {
            connection.Table<DbItem>().Where(c => true).Delete();
        }
        finally {
            ReleaseConnection(connection);
        }
    }

    // Private methods

    private async ValueTask<SQLiteConnection> AcquireConnection(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            return _connections!.Get();
        }
        catch {
            _semaphore.Release();
            throw;
        }
    }

    private void ReleaseConnection(SQLiteConnection connection)
    {
        try {
            _connections!.Return(connection);
        }
        finally {
            _semaphore.Release();
        }
    }

    // Nested types

    [Table("items")]
    public sealed class DbItem
    {
        [PrimaryKey] public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    // ReSharper disable once InconsistentNaming
    private sealed class SQLiteConnectionPoolPolicy : IPooledObjectPolicy<SQLiteConnection>
    {
        public FilePath DbPath { get; }
        public SQLiteOpenFlags DbOpenFlags { get; }

        public SQLiteConnectionPoolPolicy(FilePath dbPath, SQLiteOpenFlags dbOpenFlags)
        {
            DbPath = dbPath;
            DbOpenFlags = dbOpenFlags;
        }

        public SQLiteConnection Create()
            => new (DbPath, DbOpenFlags);

        public bool Return(SQLiteConnection obj)
        {
            if (!obj.IsInTransaction)
                return true;

            obj.DisposeSilently();
            return false;
        }
    }
}
