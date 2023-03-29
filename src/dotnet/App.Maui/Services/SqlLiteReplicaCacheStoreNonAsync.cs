using ActualChat.UI.Blazor.Services;
using SQLite;
using Stl.Locking;

namespace ActualChat.App.Maui.Services;

public class SqlLiteReplicaCacheStoreNonAsync : IReplicaCacheStore
{
    public const string DatabaseFilename = SqlLiteReplicaCacheStore.DatabaseFilename;

    public const SQLite.SQLiteOpenFlags Flags =
        // open the database in read/write mode
        SQLite.SQLiteOpenFlags.ReadWrite |
        // create the database if it doesn't exist
        SQLite.SQLiteOpenFlags.Create |
        // enable multi-threaded database access
        SQLite.SQLiteOpenFlags.SharedCache;
        //SQLite.SQLiteOpenFlags.NoMutex;

    public static string DatabasePath =>
        Path.Combine(FileSystem.AppDataDirectory, DatabaseFilename);

    private SQLiteConnection _database = null!;
    private int _rowsNumber;
    private readonly Task _whenReady;
    private readonly AsyncLock _asyncLock = new (ReentryMode.CheckedPass);

    public SqlLiteReplicaCacheStoreNonAsync()
        => _whenReady = Task.Run(Init);

    public async Task<string?> TryGetValue(string key)
    {
        await _whenReady.ConfigureAwait(false);
        using var _ = await _asyncLock.Lock().ConfigureAwait(false);
        var item = _database
            .Table<KeyValueItem>()
            .FirstOrDefault(i => i.Key == key);
        return item?.Value;
    }

    public async Task SetValue(string key, string value)
    {
        await _whenReady.ConfigureAwait(false);
        using var _ = await _asyncLock.Lock().ConfigureAwait(false);
        var item = _database
            .Table<KeyValueItem>()
            .FirstOrDefault(i => i.Key == key);
        if (item == null) {
            item = new KeyValueItem {Key = key, Value = value};
            var inserted = _database.Insert(item);
        }
        else {
            item.Value = value;
            var updated = _database.Update(item);
        }
    }

    private void Init()
    {
        _database = new SQLiteConnection(DatabasePath, Flags);
        _database.EnableWriteAheadLogging();
        var result = _database.CreateTable<KeyValueItem>();
        _rowsNumber = _database.Table<KeyValueItem>().Count();
    }
}
