using ActualChat.UI.Blazor.Services;
using SQLite;

namespace ActualChat.App.Maui.Services;

// ReSharper disable once InconsistentNaming
public class SQLiteReplicaCacheStore : IReplicaCacheStorage
{
    public static class Constants
    {
        public const string DatabaseFilename = "ReplicaCacheSQLite.db3";

        public const SQLiteOpenFlags Flags =
            // open the database in read/write mode
            SQLiteOpenFlags.ReadWrite |
            // create the database if it doesn't exist
            SQLiteOpenFlags.Create |
            // enable multi-threaded database access
            SQLiteOpenFlags.NoMutex;

        public static string DatabasePath => Path.Combine(FileSystem.AppDataDirectory, DatabaseFilename);
    }

    private readonly Task<SQLiteConnection?> _initDb;
    private readonly object _lock = new ();

    private ILogger Log { get; }

    public SQLiteReplicaCacheStore(ILogger log)
    {
        Log = log;
        _initDb = Task.Run(InitDatabase);
    }

    public async Task<string?> TryGetValue(string key)
    {
        var database = await _initDb.ConfigureAwait(false);
        if (database == null)
            return null;
        lock (_lock) {
            // TODO(DF): rewrite with simple text command
            var item = database
                .Table<KeyValueItem>()
                .FirstOrDefault(i => i.Key == key);
            return item?.Value;
        }
    }

    public async Task SetValue(string key, string value)
    {
        var database = await _initDb.ConfigureAwait(false);
        if (database == null)
            return;
        lock (_lock) {
            // TODO(DF): rewrite with single text command
            var item = database
                .Table<KeyValueItem>()
                .FirstOrDefault(i => i.Key == key);
            if (item == null) {
                item = new KeyValueItem {Key = key, Value = value};
                var inserted = database.Insert(item);
            }
            else {
                item.Value = value;
                var updated = database.Update(item);
            }
        }
    }

    private SQLiteConnection? InitDatabase()
    {
        try {
            var database = new SQLiteConnection(Constants.DatabasePath, Constants.Flags);
            database.EnableWriteAheadLogging();
            _ = database.CreateTable<KeyValueItem>();
            return database;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize SQLite replica cache database");
            return null;
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
}
