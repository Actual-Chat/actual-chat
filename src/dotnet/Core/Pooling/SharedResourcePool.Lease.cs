using Stl.Concurrency;
using Stl.Pooling;
using Stl.Reflection;

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
                switch (--_renterCount) {
                    case > 0: return;
                    case < 0: throw new InvalidOperationException(
                        "Each lease returned by Rent should be disposed just once.");
                }

                var endRentDelayTokenSource = new CancellationTokenSource();
                var endRentDelayToken = endRentDelayTokenSource.Token;
                _endRentDelayTokenSource = endRentDelayTokenSource;

                BackgroundTask.Run(async () => {
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

        internal async ValueTask<bool> BeginRent()
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
            try {
                await _resourceTask.ConfigureAwait(false);
                return true;
            }
            catch (Exception e) {
                Pool.Log.LogError(e, nameof(Pool.ResourceFactory) + " failed");
                Task endRenTask;
                lock (Lock)
                    endRenTask = _endRentTask ??= EndRent();
                await endRenTask.ConfigureAwait(false);
                return false;
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
