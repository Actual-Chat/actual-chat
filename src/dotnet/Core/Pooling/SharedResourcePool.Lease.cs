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
#pragma warning disable VSTHRD002
        public TResource Resource => _resourceTask.Result;
#pragma warning restore VSTHRD002
        public bool IsRented {
            get {
                lock (Lock) return _renterCount > 0;
            }
        }

        internal Lease(SharedResourcePool<TKey, TResource> pool, TKey key, CancellationToken cancellationToken)
        {
            Pool = pool;
            Key = key;
            _resourceTask = pool.ResourceFactory.Invoke(key, cancellationToken);
        }

        public void Dispose()
        {
            lock (Lock) {
                if (--_renterCount != 0)
                    return;

                var endRentDelayTokenSource = new CancellationTokenSource();
                var endRentDelayToken = endRentDelayTokenSource.Token;
                _endRentDelayTokenSource = endRentDelayTokenSource;

                _ = BackgroundTask.Run(async () => {
                    try {
                        await Task.Delay(Pool.ResourceDisposeDelay, endRentDelayToken).ConfigureAwait(false);
                        lock (Lock)
                            _endRentTask ??= EndRent();
                    }
                    finally {
                        endRentDelayTokenSource.CancelAndDisposeSilently();
                    }
                }, CancellationToken.None);
            }
        }

        internal async ValueTask<Task?> BeginRent(CancellationToken cancellationToken)
        {
            Task? endRentTask;
            lock (Lock) {
                ++_renterCount;
                _endRentDelayTokenSource.CancelAndDisposeSilently();
                _endRentDelayTokenSource = null;
                endRentTask = _endRentTask;
            }
            if (endRentTask != null)
                return endRentTask;

            // If we're here, _endRentTask == null, i.e. no resource is allocated yet
            try {
                await _resourceTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }
            catch (OperationCanceledException) {
                // We assume we don't need to call EndRent if _resourceTask is cancelled.
                // SharedResourcePool.Rent will fail with OperationCanceledException
                // as well in this case, which is expected outcome, coz this method
                // gets its cancellation token.
                // The only thing we need to do here is to try removing the lease from
                // the pool - EndRent, which is responsible for this otherwise,
                // won't be called in this case.
                lock (Lock) {
                    _endRentTask = Task.CompletedTask;
                    --_renterCount;
                }
                Pool._leases.TryRemove(Key, this);
                throw;
            }
            catch (Exception e) {
                Pool.Log.LogError(e, nameof(Pool.ResourceFactory) + " failed");
                lock (Lock)
                    endRentTask = _endRentTask ??= EndRent();
                return endRentTask;
            }
        }

        private async Task EndRent()
        {
            try {
                await Pool.ResourceDisposer.Invoke(Key, Resource).ConfigureAwait(false);
            }
            catch (Exception e) {
                Pool.Log.LogError(e,
                    "Failed to dispose pooled resource of type {ResourceType}", typeof(TResource).FullName);
            }
            finally {
                Pool._leases.TryRemove(Key, this);
            }
        }
    }
}
