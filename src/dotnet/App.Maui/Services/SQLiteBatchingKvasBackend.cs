using System.Diagnostics.CodeAnalysis;
using System.Text;
using ActualChat.Kvas;
using SQLite;
using Stl.IO;

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
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private ILogger? Log => _log ??= Services.LogFor(GetType());

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SQLiteBatchingKvasBackend))]
    public SQLiteBatchingKvasBackend(FilePath dbPath, string version, IServiceProvider services)
    {
        Services = services;
        _connectionPool = Initialize(dbPath, version);
    }

    public ValueTask<byte[]?[]> GetMany(string[] keys, CancellationToken cancellationToken = default)
    {
        var result = new byte[]?[keys.Length];
        if (_connectionPool == null)
            return ValueTask.FromResult(result);

        using var lease = _connectionPool.Rent();
        for (var i = 0; i < keys.Length; i++) {
            var dbItem = lease.Resource.Find<DbItem?>(keys[i]);
            result[i] = dbItem?.Value;
        }
        return ValueTask.FromResult(result);
    }

    public Task SetMany(List<(string Key, byte[]? Value)> updates, CancellationToken cancellationToken = default)
    {
        if (_connectionPool == null)
            return Task.CompletedTask;

        using var lease = _connectionPool.Rent();
        foreach (var (key, value) in updates) {
            if (value == null)
                lease.Resource.Delete<DbItem>(key);
            else
                lease.Resource.InsertOrReplace(new DbItem { Key = key, Value = value });
        }
        return Task.CompletedTask;
    }

    public Task Clear(CancellationToken cancellationToken = default)
    {
        if (_connectionPool == null)
            return Task.CompletedTask;

        using var lease = _connectionPool.Rent();
        lease.Resource.DeleteAll<DbItem>();
        return Task.CompletedTask;
    }

    // Private methods

    private SimpleConcurrentPool<SQLiteConnection>? Initialize(FilePath dbPath, string version)
    {
        try {
            var connectionCount = HardwareInfo.ProcessorCount + 2;
            var connections = new SimpleConcurrentPool<SQLiteConnection>(
                () => new(dbPath, OpenFlags),
                static c => !c.IsInTransaction,
                connectionCount);
            using var lease = connections.Rent();
            var connection = lease.Resource;
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
            return connections;
        }
        catch (Exception e) {
            Log?.LogError(e, "Failed to initialize SQLite database");
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
}
