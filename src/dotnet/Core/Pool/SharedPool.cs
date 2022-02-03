namespace ActualChat.Pool;

public interface ILease<TValue> : IDisposable
{
    TValue Value { get; }
}

public class SharedPool<TKey,TValue>
    where TValue : class
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, LeaseBag> _leases = new ();
    private Func<TKey,Task<TValue>> Factory { get; }
    private double ValueDisposeDelay { get; }

    public SharedPool(Func<TKey,Task<TValue>> factory, double valueDisposeDelay = 10)
    {
        Factory = factory;
        ValueDisposeDelay = valueDisposeDelay;
    }

    public async Task<ILease<TValue>> Lease(TKey key)
        => await _leases.GetOrAdd(key, static (key1, pool) => new LeaseBag(pool, key1), this)
            .InitLease()
            .ConfigureAwait(false);

    public sealed class LeaseBag : ILease<TValue>
    {
        private int _leaseCount;
        private Task<TValue>? _valueTask;
        private SharedPool<TKey, TValue> SharedPool { get; }
        public TKey Key { get; }
        public TValue Value => _valueTask?.Result ?? throw new InvalidOperationException();

        internal LeaseBag(SharedPool<TKey,TValue> sharedPool, TKey key)
        {
            SharedPool = sharedPool;
            Key = key;
        }

        internal async Task<LeaseBag> InitLease()
        {
            Task<TValue>? valueTask;
            if (Interlocked.CompareExchange(ref _leaseCount, 1, 0) == 0) {
                valueTask = SharedPool.Factory.Invoke(Key);
                Volatile.Write(ref _valueTask, valueTask);
            }
            else {
                var spin = new SpinWait();
                valueTask = Volatile.Read(ref _valueTask);
                while (valueTask == null) {
                    spin.SpinOnce();
                    valueTask = Volatile.Read(ref _valueTask);
                }
                Interlocked.Increment(ref _leaseCount);
            }

            await valueTask.ConfigureAwait(false);

            return this;
        }

        void IDisposable.Dispose()
        {
            // if the last lease is being disposed
            if (Interlocked.CompareExchange(ref _leaseCount, 0, 1) == 1) {
                var disposeDelay = SharedPool.ValueDisposeDelay;
                if (disposeDelay > 0)
                    Task.Run(async () => {
                        await Task.Delay(TimeSpan.FromSeconds(disposeDelay)).ConfigureAwait(false);

                        // check whether we still don't have new leases
                        if (Interlocked.CompareExchange(ref _leaseCount, -1, 0) == 0)
                            if (SharedPool._leases.TryRemove(Key, out var bag))
                                (bag.Value as IDisposable)?.Dispose();
                    });
                else {
                    if (SharedPool._leases.TryRemove(Key, out var bag))
                        (bag.Value as IDisposable)?.Dispose();
                }
            }
            else
                Interlocked.Decrement(ref _leaseCount);
        }
    }
}
