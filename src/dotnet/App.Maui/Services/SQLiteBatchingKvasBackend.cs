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

    private readonly Task<ObjectPool<SQLiteConnection>?> _whenConnectionPoolReady;
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private ILogger? Log => _log ??= Services.LogFor(GetType());

    public SQLiteBatchingKvasBackend(FilePath dbPath, string version, IServiceProvider services)
    {
        Services = services;
        _whenConnectionPoolReady = Task.Run(() => Initialize(dbPath, version));
    }

    public async ValueTask<byte[]?[]> GetMany(string[] keys, CancellationToken cancellationToken = default)
    {
        var result = new byte[]?[keys.Length];
        var connection = await AcquireConnection().ConfigureAwait(false);
        if (connection == null)
            return result;

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
        var connection = await AcquireConnection().ConfigureAwait(false);
        if (connection == null)
            return;

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
        var connection = await AcquireConnection().ConfigureAwait(false);
        if (connection == null)
            return;

        try {
            connection.DeleteAll<DbItem>();
        }
        finally {
            ReleaseConnection(connection);
        }
    }

    // Private methods

    private ObjectPool<SQLiteConnection>? Initialize(FilePath dbPath, string version)
    {
        try {
            var connectionsPolicy = new SQLiteConnectionPoolPolicy(dbPath);
            var connectionCount = HardwareInfo.ProcessorCount + 2;
            var connections = new DefaultObjectPool<SQLiteConnection>(connectionsPolicy, connectionCount);
            var connection = connections.Get();
            connection.EnableWriteAheadLogging();
            var versionBytes = Encoding.UTF8.GetEncoder().Convert(version);
            if (connection.CreateTable<DbItem>() == CreateTableResult.Migrated) {
                var existingVersionBytes = connection.Find<DbItem>(VersionKey)?.Value ?? Array.Empty<byte>();
                if (!versionBytes.AsSpan().SequenceEqual(existingVersionBytes.AsSpan())) {
                    _ = connection.DropTable<DbItem>();
                    _ = connection.CreateTable<DbItem>();
                }
            }
            connection.InsertOrReplace(new DbItem { Key = VersionKey, Value = versionBytes });
            connections.Return(connection);
            return connections;
        }
        catch (Exception e) {
            Log?.LogError(e, "Failed to initialize SQLite database");
            return null;
        }
    }

    private async ValueTask<SQLiteConnection?> AcquireConnection()
    {
        var connections = await _whenConnectionPoolReady.ConfigureAwait(false);
        return connections?.Get();
    }

    private void ReleaseConnection(SQLiteConnection connection)
#pragma warning disable VSTHRD002
        => _whenConnectionPoolReady.Result!.Return(connection);
#pragma warning restore VSTHRD002

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
