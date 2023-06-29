using System.Text;
using ActualChat.Kvas;
using Microsoft.Extensions.ObjectPool;
using SQLite;
using Stl.IO;

namespace ActualChat.App.Maui.Services;

// ReSharper disable once InconsistentNaming
public sealed class SQLiteBatchingKvasBackend : IBatchingKvasBackend
{
    public const string VersionKey = "(version)";

    private readonly ObjectPool<SQLiteConnection>? _connections;
    private readonly SemaphoreSlim _semaphore;
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private ILogger? Log => _log ??= Services.LogFor(GetType());

    public Task WhenInitialized => Task.CompletedTask;

    public SQLiteBatchingKvasBackend(FilePath dbPath, string version, IServiceProvider services)
    {
        Services = services;
        _semaphore = new SemaphoreSlim(HardwareInfo.ProcessorCount);
        try {
            var connectionsPolicy = new SQLiteConnectionPoolPolicy(dbPath);
            _connections = new DefaultObjectPool<SQLiteConnection>(connectionsPolicy, HardwareInfo.ProcessorCount + 2);
            var connection = _connections.Get();
            try {
                connection.EnableWriteAheadLogging();
                var versionBytes = Encoding.UTF8.GetEncoder().Convert(version);
                if (connection.CreateTable<DbItem>() == CreateTableResult.Migrated) {
                    var existingVersionBytes = connection.Find<DbItem>(VersionKey)?.Value;
                    var existingVersionSpan = existingVersionBytes != null ? existingVersionBytes.AsSpan() : default;
                    if (existingVersionBytes == null || !versionBytes.AsSpan().SequenceEqual(existingVersionSpan)) {
                        _ = connection.DropTable<DbItem>();
                        _ = connection.CreateTable<DbItem>();
                    }
                }
                connection.InsertOrReplace(new DbItem { Key = VersionKey, Value = versionBytes });
            }
            finally {
                _connections.Return(connection);
            }
        }
        catch (Exception e) {
            _connections = null;
            Log?.LogError(e, "Failed to initialize SQLite database");
        }
    }

    public async Task<byte[]?[]> GetMany(string[] keys, CancellationToken cancellationToken = default)
    {
        var result = new byte[]?[keys.Length];
        if (_connections == null)
            return result;

        var connection = await AcquireConnection(cancellationToken).ConfigureAwait(false);
        try {
            for (var i = 0; i < keys.Length; i++) {
                var dbItem = connection.Find<DbItem?>(keys[i]);
                result[i] = dbItem?.Value;
            }
            return result;
        }
        finally {
            ReleaseConnection(connection);
        }
    }

    public async Task SetMany(List<(string Key, byte[]? Value)> updates, CancellationToken cancellationToken = default)
    {
        if (_connections == null)
            return;

        var connection = await AcquireConnection(cancellationToken).ConfigureAwait(false);
        try {
            foreach (var (key, value) in updates) {
                if (value == null)
                    connection.Delete<DbItem>(key);
                else
                    connection.InsertOrReplace(new DbItem { Key = key, Value = value });
            }
        }
        finally {
            ReleaseConnection(connection);
        }
    }

    public async Task Clear(CancellationToken cancellationToken = default)
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

        public SQLiteConnectionPoolPolicy(FilePath dbPath)
        {
            DbPath = dbPath;
            OpenFlags =
                // Open the database in read/write mode
                SQLiteOpenFlags.ReadWrite |
                // Create the database if it doesn't exist
                SQLiteOpenFlags.Create |
                // Assume each connection is never used concurrently
                SQLiteOpenFlags.NoMutex;
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
