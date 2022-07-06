namespace ActualChat.Db.Module;

public class OutstandingDbConnectionsCounter
{
    private readonly object _syncObject = new object();
    private readonly Dictionary<string, int> _counters = new ();
    private int _index;

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

    public bool CheckDump()
    {
        lock (_syncObject) {
            _index++;
            if (_index >= 20) {
                _index = 0;
                return true;
            }
            return false;
        }
    }
}
