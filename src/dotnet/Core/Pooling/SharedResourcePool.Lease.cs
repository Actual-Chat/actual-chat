using ActualLab.Pooling;

namespace ActualChat.Pooling;

#pragma warning disable VSTHRD002

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
                lock (Lock)
                    return _renterCount > 0;
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
            CancellationTokenSource endRentDelayTokenSource;
            CancellationToken endRentDelayToken;
            lock (Lock) {
                _renterCount = Math.Max(0, _renterCount - 1);
                if (_renterCount != 0 || _endRentTask != null)
                    return;
                if (_endRentDelayTokenSource != null)
                    return; // Weird case, shouldn't happen

                endRentDelayTokenSource = new CancellationTokenSource();
                endRentDelayToken = endRentDelayTokenSource.Token;
                _endRentDelayTokenSource = endRentDelayTokenSource;
            }

            // Start delayed EndRent
            _ = Task
                .Delay(Pool.ResourceDisposeDelay, endRentDelayToken)
                .ContinueWith(delayTask => {
                    if (delayTask.IsCanceled)
                        return;

                    Monitor.Enter(Lock);
                    try {
                        // It's possible that BeginRent was called up right before we entered this lock.
                        // BeginRent sets _endRentDelayTokenSource to null when it succeeds,
                        // so all we need to do here is to check if it's still the same.
                        if (_endRentDelayTokenSource != endRentDelayTokenSource)
                            return;

                        _endRentDelayTokenSource = null;
                        _endRentTask ??= EndRent();
                    }
                    finally {
                        Monitor.Exit(Lock);
                        endRentDelayTokenSource.Dispose();
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        internal async ValueTask<Task?> BeginRent(CancellationToken cancellationToken)
        {
            lock (Lock) {
                if (_endRentTask != null)
                    return _endRentTask;

                ++_renterCount;
                _endRentDelayTokenSource.CancelAndDisposeSilently();
                _endRentDelayTokenSource = null;
            }

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
                    _renterCount = 0;
                    _endRentTask = Task.CompletedTask;
                }
                Pool._leases.TryRemove(Key, this);
                throw;
            }
            catch (Exception e) {
                Pool.Log.LogError(e, nameof(Pool.ResourceFactory) + " failed");
                lock (Lock) {
                    _renterCount = 0;
                    return _endRentTask ??= EndRent();
                }
            }
        }

        private async Task EndRent()
        {
            try {
                await Pool.ResourceDisposer.Invoke(Key, Resource).ConfigureAwait(false);
            }
            catch (Exception e) {
                Pool.Log.LogError(e,
                    "Failed to dispose pooled resource of type {ResourceType}",
                    typeof(TResource).GetName());
            }
            finally {
                Pool._leases.TryRemove(Key, this);
            }
        }
    }
}
