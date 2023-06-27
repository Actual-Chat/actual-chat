using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.ObjectPool;
using SQLite;
using Stl.IO;
using Stl.Rpc.Caching;

namespace ActualChat.App.Maui.Services;

#pragma warning disable MA0056
#pragma warning disable MA0064

// ReSharper disable once InconsistentNaming
public sealed class SQLiteClientComputedCache : AppClientComputedCache
{
    private const SQLiteOpenFlags DbOpenFlags =
        // Open the database in read/write mode
        SQLiteOpenFlags.ReadWrite |
        // Create the database if it doesn't exist
        SQLiteOpenFlags.Create |
        // Assume each connection is never used concurrently
        SQLiteOpenFlags.NoMutex;

    private static readonly TextOrBytes? Null = default;

    private readonly ILruCache<RpcCacheKey, TextOrBytes> _fetchCache;
    private readonly ObjectPool<SQLiteConnection>? _connections;
    private readonly SemaphoreSlim _semaphore;

    public SQLiteClientComputedCache(Options settings, IServiceProvider services)
        : base(settings, services, false)
    {
        _fetchCache = new ThreadSafeLruCache<RpcCacheKey, TextOrBytes>(128);
        _semaphore = new SemaphoreSlim(HardwareInfo.ProcessorCount);

        try {
            var connectionsPolicy = new SQLiteConnectionPoolPolicy(Settings.DbPath, DbOpenFlags);
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

        // ReSharper disable once VirtualMemberCallInConstructor
        WhenInitialized = Initialize(settings.Version);
    }

    protected override async ValueTask<TextOrBytes?> Fetch(RpcCacheKey key, CancellationToken cancellationToken)
    {
        if (_connections == null)
            return null;

        var connection = await AcquireConnection(cancellationToken).ConfigureAwait(false);
        try {
            var dbItem = connection.Find<DbItem?>(key.ToString());
            return dbItem is { Value: var vValue } ? new TextOrBytes(vValue) : Null;
        }
        finally {
            ReleaseConnection(connection);
        }
    }

    public override void Set(RpcCacheKey key, TextOrBytes value)
    {
        if (_fetchCache.TryGetValue(key, out var cachedValue) && cachedValue.DataEquals(value))
            return;

        base.Set(key, value);
    }

    public override void Remove(RpcCacheKey key)
    {
        _fetchCache.Remove(key);
        base.Remove(key);
    }

    public override async Task Clear(CancellationToken cancellationToken = default)
    {
        if (_connections == null)
            return;

        var connection = await AcquireConnection(cancellationToken).ConfigureAwait(false);
        try {
            connection.DeleteAll<DbItem>();
        }
        finally {
            ReleaseConnection(connection);
        }
    }

    protected override async Task Flush(Dictionary<RpcCacheKey, TextOrBytes?> flushingQueue)
    {
        if (_connections == null)
            return;

        var connection = await AcquireConnection().ConfigureAwait(false);
        try {
            foreach (var (key, value) in flushingQueue) {
                if (value is not { } vValue)
                    connection.Delete<DbItem>(key.ToString());
                else {
                    var item = new DbItem { Key = key.ToString(), Value = vValue.Bytes };
                    connection.InsertOrReplace(item);
                }
            }
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
        public byte[] Value { get; set; } = null!;
    }

    // ReSharper disable once InconsistentNaming
    private sealed class SQLiteConnectionPoolPolicy : IPooledObjectPolicy<SQLiteConnection>
    {
        private FilePath DbPath { get; }
        private SQLiteOpenFlags OpenFlags { get; }

        public SQLiteConnectionPoolPolicy(FilePath dbPath, SQLiteOpenFlags openFlags)
        {
            DbPath = dbPath;
            OpenFlags = openFlags;
        }

        public SQLiteConnection Create()
            => new (DbPath, OpenFlags);

        public bool Return(SQLiteConnection obj)
        {
            if (!obj.IsInTransaction)
                return true;

            obj.DisposeSilently();
            return false;
        }
    }
}
