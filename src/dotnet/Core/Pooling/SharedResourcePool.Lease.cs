using Stl.Concurrency;
using Stl.Pooling;

namespace ActualChat.Pooling;

public partial class SharedResourcePool<TKey, TResource>
{
    public sealed class Lease : IResourceLease<TResource>
    {
        private readonly Task<TResource> _resourceTask;
        private Task? _endRentTask;
        private CancellationTokenSource? _endRentDelayTokenSource;
        private int _renterCount;
        private object Lock => _resourceTask;

        public SharedResourcePool<TKey, TResource> Pool { get; }
        public TKey Key { get; }
        public TResource Resource => _resourceTask.Result;
        public bool IsRented {
            get {
                lock (Lock) {
                    return _renterCount > 0;
                }
            }
        }

        internal Lease(SharedResourcePool<TKey, TResource> pool, TKey key)
        {
            Pool = pool;
            Key = key;
            _resourceTask = pool.ResourceFactory.Invoke(key);
        }

        public void Dispose()
        {
            lock (Lock) {
                switch (--_renterCount) {
                    case > 0: return;
                    case < 0: throw new ObjectDisposedException("One of the leases seem to be disposed more than once.");
                }

                var endRentDelayTokenSource = new CancellationTokenSource();
                var endRentDelayToken = endRentDelayTokenSource.Token;
                _endRentDelayTokenSource = endRentDelayTokenSource;

                using var _1 = ExecutionContextExt.SuppressFlow();
                _ = Task.Run(async () => {
                    try {
                        await Task.Delay(Pool.ResourceDisposeDelay, endRentDelayToken);
                        lock (Lock) {
                            _endRentTask = EndRent();
                        }
                    }
                    finally {
                        endRentDelayTokenSource.CancelAndDisposeSilently();
                    }
                }, CancellationToken.None);
            }
        }

        internal async ValueTask<bool> BeginRent(CancellationToken cancellationToken)
        {
            Task? endRentTask;
            lock (Lock) {
                ++_renterCount;
                if (_endRentDelayTokenSource != null) {
                    _endRentDelayTokenSource.CancelAndDisposeSilently();
                    _endRentDelayTokenSource = null;
                }
                endRentTask = _endRentTask;
            }
            if (endRentTask != null) {
                await endRentTask.ConfigureAwait(false);
                return false;
            }
            await _resourceTask.ConfigureAwait(false);
            return true;
        }

        private async Task EndRent()
        {
            var resource = Resource;
            if (resource is IAsyncDisposable ad)
                await ad.DisposeAsync().ConfigureAwait(false);
            else if (resource is IDisposable d)
                d.Dispose();
            Pool._leases.TryRemove(Key, this);
        }
    }
}
