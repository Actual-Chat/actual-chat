namespace ActualChat.Db.Module;

public class OutstandingDbConnectionsCounter
{
    private readonly object _syncObject = new object();
    private readonly Dictionary<string, int> _counters = new ();

    public int Increment(string dbName)
    {
        lock (_syncObject) {
            if (!_counters.TryGetValue(dbName, out var value))
                value = 0;
            value++;
            _counters[dbName] = value;
            return value;
        }
    }

    public int Decrement(string dbName)
    {
        lock (_syncObject) {
            if (!_counters.TryGetValue(dbName, out var value))
                value = 0;
            value--;
            _counters[dbName] = value;
            return value;
        }
    }
}
