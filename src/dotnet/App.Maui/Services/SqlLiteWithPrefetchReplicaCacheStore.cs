using ActualChat.UI.Blazor.Services;
using SQLite;
using Stl.Locking;

namespace ActualChat.App.Maui.Services;

public class SqlLiteWithPrefetchReplicaCacheStore : IReplicaCacheStore
{
    public const string DatabaseFilename = SqlLiteReplicaCacheStore.DatabaseFilename;

    public const SQLite.SQLiteOpenFlags Flags =
        // open the database in read/write mode
        SQLite.SQLiteOpenFlags.ReadWrite |
        // create the database if it doesn't exist
        SQLite.SQLiteOpenFlags.Create |
        // enable multi-threaded database access
        //SQLite.SQLiteOpenFlags.SharedCache;
        SQLite.SQLiteOpenFlags.NoMutex;

    public static string DatabasePath =>
        Path.Combine(FileSystem.AppDataDirectory, DatabaseFilename);

    private SQLiteAsyncConnection _database = null!;
    private readonly ConcurrentDictionary<string, string> _memCache = new (StringComparer.Ordinal);
    private readonly Task _whenReady;
    private readonly AsyncLock _asyncLock = new (ReentryMode.CheckedPass);

    public SqlLiteWithPrefetchReplicaCacheStore()
        => _whenReady = Init();

    public async Task<string?> TryGetValue(string key)
    {
        await _whenReady.ConfigureAwait(false);
        _memCache.TryGetValue(key, out var value);
        return value;
    }

    public async Task SetValue(string key, string value)
    {
        await _whenReady.ConfigureAwait(false);
        using var _ = await _asyncLock.Lock().ConfigureAwait(false);
        _memCache.TryRemove(key, out var _);
        var item = await _database
            .Table<KeyValueItem>()
            .Where(i => i.Key == key)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        if (item == null) {
            item = new KeyValueItem {Key = key, Value = value};
            var inserted = await _database.InsertAsync(item).ConfigureAwait(false);
        }
        else {
            item.Value = value;
            var updated = await _database.UpdateAsync(item).ConfigureAwait(false);
        }
    }

    private async Task Init()
    {
        try {
            _database = new SQLiteAsyncConnection(DatabasePath, Flags);
            await _database.EnableWriteAheadLoggingAsync().ConfigureAwait(false);
            var result = await _database.CreateTableAsync<KeyValueItem>().ConfigureAwait(false);
            var items = await _database.Table<KeyValueItem>().ToArrayAsync().ConfigureAwait(false);
            foreach (var item in items)
                _memCache.TryAdd(item.Key, item.Value);
        }
        catch (Exception e) {
            throw;
        }
    }
}
