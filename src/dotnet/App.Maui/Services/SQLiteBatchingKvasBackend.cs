using System.Diagnostics.CodeAnalysis;
using System.Text;
using ActualChat.Kvas;
using SQLite;
using Stl.Concurrency;
using Stl.IO;
using Stl.Pooling;

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

    private readonly Task<ConcurrentPool<SQLiteConnection>?> _whenConnectionPoolReady;
    private readonly FilePath _dbPath;
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private ILogger? Log => _log ??= Services.LogFor(GetType());

    public SQLiteBatchingKvasBackend(FilePath dbPath, string version, IServiceProvider services)
    {
        _dbPath = dbPath;
        Services = services;
        _whenConnectionPoolReady = Task.Run(() => Initialize(dbPath, version));
    }

    public async ValueTask<byte[]?[]> GetMany(string[] keys, CancellationToken cancellationToken = default)
    {
        var result = new byte[]?[keys.Length];
        using var lease = await AcquireConnection().ConfigureAwait(false);
        if (!lease.IsValid(out var connection))
            return result;

        for (var i = 0; i < keys.Length; i++) {
            var dbItem = connection.Find<DbItem?>(keys[i]);
            result[i] = dbItem?.Value;
        }
        return result;
    }

    public async Task SetMany(List<(string Key, byte[]? Value)> updates, CancellationToken cancellationToken = default)
    {
        using var lease = await AcquireConnection().ConfigureAwait(false);
        if (!lease.IsValid(out var connection))
            return;

        foreach (var (key, value) in updates) {
            if (value == null)
                connection.Delete<DbItem>(key);
            else
                connection.InsertOrReplace(new DbItem { Key = key, Value = value });
        }
    }

    public async Task Clear(CancellationToken cancellationToken = default)
    {
        using var lease = await AcquireConnection().ConfigureAwait(false);
        if (!lease.IsValid(out var connection))
            return;

        connection.DeleteAll<DbItem>();
    }

    // Private methods

    private ConcurrentPool<SQLiteConnection>? Initialize(FilePath dbPath, string version)
    {
        try {
            var connectionCount = HardwareInfo.ProcessorCount + 2;
            var connections = new ConcurrentPool<SQLiteConnection>(() => new(_dbPath, OpenFlags), connectionCount, 1);
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

    private async ValueTask<SQLiteConnectionLease> AcquireConnection()
    {
        var connections = await _whenConnectionPoolReady.ConfigureAwait(false);
        return connections == null ? default
            : new SQLiteConnectionLease(connections.Rent()!);
    }

    // Nested types

    [Table("items")]
    public sealed class DbItem
    {
        [PrimaryKey] public string Key { get; set; } = "";
        public byte[] Value { get; set; } = null!;
    }

    // ReSharper disable once InconsistentNaming
    private readonly struct SQLiteConnectionLease(ResourceLease<SQLiteConnection?> lease) : IDisposable
    {
        public bool IsValid([NotNullWhen(true)] out SQLiteConnection? connection)
        {
            connection = lease.Resource;
            return connection != null;
        }

        public void Dispose()
        {
            if (lease.Resource is { IsInTransaction: false })
                lease.Dispose();
        }
    }
}
