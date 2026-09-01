using ActualLab.Locking;

namespace ActualChat;

/// <summary>
/// A keyed stash that lets an update operation hand a freshly produced value
/// to a compute method that is about to recompute it for the same key.
/// A per-key <see cref="AsyncLockSet{TKey}"/> serializes updates for the same key.
/// </summary>
public sealed class LockingComputeMethodPrimer<TKey, TValue>(
    Func<TKey, CancellationToken, Task<TValue>> caller,
    AsyncLockSet<TKey> locks,
    IEqualityComparer<TKey>? keyComparer = null)
    where TKey : notnull
{
    private const int StateEmpty = 0;
    private const int StateHasValue = 1;
    private const int StateDisposed = 2;

    private readonly Func<TKey, CancellationToken, Task<TValue>> _caller = caller;
    private readonly ConcurrentDictionary<TKey, Primer> _reservations
        = new(keyComparer ?? EqualityComparer<TKey>.Default);

    public LockingComputeMethodPrimer(
        Func<TKey, CancellationToken, Task<TValue>> caller,
        LockReentryMode reentryMode = LockReentryMode.CheckedFail,
        IEqualityComparer<TKey>? keyComparer = null)
        : this(
            caller,
            new AsyncLockSet<TKey>(
                reentryMode,
                AsyncLockSet<TKey>.DefaultConcurrencyLevel,
                AsyncLockSet<TKey>.DefaultCapacity,
                keyComparer),
            keyComparer)
    { }

    public async ValueTask<Primer> LockAndPrepare(TKey key, CancellationToken cancellationToken = default)
    {
        var releaser = await locks.Lock(key, cancellationToken).ConfigureAwait(false);
        try {
            var reservation = new Primer(this, key, releaser);
            _reservations[key] = reservation;
            return reservation;
        }
        catch {
            releaser.Dispose();
            throw;
        }
    }

    public async ValueTask<Primer?> TryLockAndPrepare(
        TKey key,
        Func<bool> mustProceed,
        CancellationToken cancellationToken = default)
    {
        // Returns null once mustProceed stops holding - typically it tracks the consistency of the
        // computed whose value is about to be replaced. Evaluated before taking the lock, to skip
        // queuing for it at all, and again after, since a writer can land in between.
        if (!mustProceed.Invoke())
            return null;

        var primer = await LockAndPrepare(key, cancellationToken).ConfigureAwait(false);
        if (mustProceed.Invoke())
            return primer;

        primer.Dispose();
        return null;
    }

    public bool TryUsePrimed(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_reservations.TryGetValue(key, out var reservation))
            return reservation.TryUse(out value);

        value = default;
        return false;
    }

    // Nested types

    /// <summary>
    /// A single stash slot reserved via
    /// <see cref="LockingComputeMethodPrimer{TKey,TValue}.LockAndPrepare"/>.
    /// Holds the per-key lock until disposed; also holds the stashed value (if any).
    /// </summary>
    public sealed class Primer : IDisposable
    {
        private AsyncLockSet<TKey>.Releaser _releaser;
        private int _state;
        private TValue? _value;

        public LockingComputeMethodPrimer<TKey, TValue> Owner { get; }
        public TKey Key { get; }
        public bool HasValue => Volatile.Read(ref _state) == StateHasValue;

        internal Primer(LockingComputeMethodPrimer<TKey, TValue> owner, TKey key, AsyncLockSet<TKey>.Releaser releaser)
        {
            Owner = owner;
            Key = key;
            _releaser = releaser;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _state, StateDisposed) == StateDisposed)
                return;

            _value = default;
            Owner._reservations.TryRemove(Key, this);
            _releaser.Dispose();
        }

        public async ValueTask Prime(TValue value, CancellationToken cancellationToken = default)
        {
            _value = value;
            if (Interlocked.CompareExchange(ref _state, StateHasValue, StateEmpty) == StateDisposed) {
                _value = default;
                throw new ObjectDisposedException(nameof(Primer));
            }

            // Invalidate existing value
            using (Invalidation.Begin())
                _ = Owner._caller.Invoke(Key, default);

            // We assume the caller will try to pull the value during this call
            await Owner._caller.Invoke(Key, cancellationToken).ConfigureAwait(false);
        }

        internal bool TryUse([MaybeNullWhen(false)] out TValue value)
        {
            if (Interlocked.CompareExchange(ref _state, StateEmpty, StateHasValue) != StateHasValue) {
                value = default;
                return false;
            }

            value = _value!;
            _value = default;
            return true;
        }
    }
}
