namespace ActualChat.Messaging;

/// <summary>
/// Orders items by partition key: at most one item per partition runs at a time,
/// different partitions run in parallel. It's a coordinator, not a worker -
/// the caller runs what <see cref="Update"/> and <see cref="OnCompleted"/> hand back.
/// </summary>
public sealed class PartitionedCommandQueue<TItem>
    where TItem : class
{
    private readonly ConcurrentDictionary<string, Lane> _lanes = new();

    public event Action? Changed;

    public TItem? Update(string partitionKey, Func<IReadOnlyList<TItem>, QueueEdits<TItem>> update)
    {
        var toRun = _lanes.GetOrAdd(partitionKey, static _ => new Lane()).Update(update);
        Changed?.Invoke();
        return toRun;
    }

    public TItem? OnCompleted(string partitionKey)
    {
        var next = _lanes.TryGetValue(partitionKey, out var lane) ? lane.OnCompleted() : null;
        Changed?.Invoke();
        return next;
    }

    public IReadOnlyList<TItem> GetPending(string partitionKey)
        => _lanes.TryGetValue(partitionKey, out var lane) ? lane.GetPending() : [];

    public int GetPendingCount(string partitionKey)
        => _lanes.TryGetValue(partitionKey, out var lane) ? lane.PendingCount : 0;

    // Nested types

    private sealed class Lane
    {
        private readonly Lock _lock = new();
        private readonly List<TItem> _pending = new();
        private bool _isRunning;

        public int PendingCount { get { lock (_lock) return _pending.Count; } }

        public IReadOnlyList<TItem> GetPending()
        {
            lock (_lock)
                return _pending.ToArray();
        }

        public TItem? Update(Func<IReadOnlyList<TItem>, QueueEdits<TItem>> update)
        {
            lock (_lock) {
                update(_pending.ToArray()).ApplyTo(_pending);
                if (_isRunning || _pending.Count == 0)
                    return null;

                _isRunning = true;
                return Dequeue();
            }
        }

        public TItem? OnCompleted()
        {
            lock (_lock) {
                if (_pending.Count == 0) {
                    _isRunning = false;
                    return null;
                }

                return Dequeue();
            }
        }

        // Private methods

        private TItem Dequeue()
        {
            var head = _pending[0];
            _pending.RemoveAt(0);
            return head;
        }
    }
}
