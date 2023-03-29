using ActualChat.UI.Blazor.Services;
using SQLite;
using Stl.Locking;

namespace ActualChat.App.Maui.Services;

public class SqlLiteReplicaCacheStore : IReplicaCacheStore
{
    public const string DatabaseFilename = "ReplicaCacheSQLite.db3";

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

    private SQLiteAsyncConnection _database = null!;
    private int _rowsNumber;
    private readonly Task _whenReady;
    private readonly AsyncLock _asyncLock = new (ReentryMode.CheckedPass);

    public SqlLiteReplicaCacheStore()
        => _whenReady = Init();

    public async Task<string?> TryGetValue(string key)
    {
        await _whenReady.ConfigureAwait(false);
        //using var _ = await _asyncLock.Lock().ConfigureAwait(false);
        var item = await _database.Table<KeyValueItem>()
            .Where(i => i.Key == key)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        return item?.Value;
    }

    public async Task SetValue(string key, string value)
    {
        await _whenReady.ConfigureAwait(false);
        using var _ = await _asyncLock.Lock().ConfigureAwait(false);
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
        _database = new SQLiteAsyncConnection(DatabasePath, Flags);
        await _database.EnableWriteAheadLoggingAsync().ConfigureAwait(false);
        var result = await _database.CreateTableAsync<KeyValueItem>().ConfigureAwait(false);
        _rowsNumber = await _database.Table<KeyValueItem>().CountAsync().ConfigureAwait(false);
    }
}

[Table("items")]
public class KeyValueItem
{
    // PrimaryKey is typically numeric
    [PrimaryKey, AutoIncrement, Column("_id")]
    public int Id { get; set; }

    [Indexed(Unique = true)]
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}
