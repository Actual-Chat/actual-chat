
namespace ActualChat;

/// <summary>
/// A keyed stash that hands a freshly produced value to a compute method that is about to recompute
/// it for the same key, gated by a monotonically-increasing version per key.
/// <para>
/// Unlike <see cref="LockingComputeMethodPrimer{TKey,TValue}"/>, this variant has no locks and no
/// reservation lifecycle — ordering is enforced by <typeparamref name="TVersion"/>. A prime is
/// accepted only if its version is strictly greater than the version currently stashed (or last
/// drained) for that key. This makes the primer safe against out-of-order invocation of its
/// producers (e.g., <c>AddCompletionHandler</c> handlers for concurrent writes that race after
/// their DB transactions commit).
/// </para>
/// <para>
/// Entries self-evict after <see cref="EntryLifetime"/> via the shared <see cref="Timeouts.Generic"/>
/// timer set — no per-entry timer task. Pick a lifetime longer than any plausible handler-dispatch
/// delay so the version floor survives long enough to catch late-arriving stale primes.
/// </para>
/// </summary>
public sealed class VersionedComputeMethodPrimer<TKey, TVersion, TValue>(
    Func<TKey, CancellationToken, Task<TValue>> caller,
    IComparer<TVersion>? versionComparer = null,
    IEqualityComparer<TKey>? keyComparer = null)
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Entry> _entries = new(keyComparer ?? EqualityComparer<TKey>.Default);
    private readonly IComparer<TVersion> _versionComparer = versionComparer ?? Comparer<TVersion>.Default;

    public TimeSpan EntryLifetime { get; init; } = TimeSpan.FromSeconds(10);

    public Task Prime(TKey key, TVersion version, TValue value, CancellationToken cancellationToken = default)
    {
        if (!TryAddOrUpdate(key, version, value))
            return Task.CompletedTask;

        // Invalidate the existing value
        using (Invalidation.Begin())
            _ = caller.Invoke(key, default);

        // We assume the caller will try to pull the value during this call
        return caller.Invoke(key, cancellationToken);
    }

    public bool TryUsePrimed(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_entries.TryGetValue(key, out var entry))
            return entry.TryUse(out value);

        value = default;
        return false;
    }

    // Private methods

    private bool TryAddOrUpdate(TKey key, TVersion version, TValue value)
    {
        while (true) {
            if (_entries.TryGetValue(key, out var prev)) {
                if (_versionComparer.Compare(version, prev.Version) <= 0)
                    return false; // A greater-or-equal version is already stashed

                var next = new Entry(_entries, key, version, value);
                if (!_entries.TryUpdate(key, next, prev))
                    continue; // CAS lost — retry.

                CancelExpire(prev);
                ScheduleExpire(next);
                return true;
            }
            else {
                var next = new Entry(_entries, key, version, value);
                if (!_entries.TryAdd(key, next))
                    continue; // CAS lost — retry.

                ScheduleExpire(next);
                return true;
            }
        }
    }

    private void ScheduleExpire(Entry entry)
        => Timeouts.Generic.AddOrUpdate(new GenericTimeoutSlot(entry, null), Timeouts.Clock.Now + EntryLifetime);

    private static void CancelExpire(Entry entry)
        => Timeouts.Generic.Remove(new GenericTimeoutSlot(entry, null));

    // Nested types

    private sealed class Entry(
        ConcurrentDictionary<TKey, Entry> entries,
        TKey key,
        TVersion version,
        TValue value
        ) : IGenericTimeoutHandler
    {
        private int _hasValue = 1;
        private TValue _value = value;

        public readonly TKey Key = key;
        public readonly TVersion Version = version;

        public bool TryUse([MaybeNullWhen(false)] out TValue value)
        {
            if (Interlocked.Exchange(ref _hasValue, 0) == 0) {
                value = default;
                return false;
            }

            value = _value!;
            _value = default!; // Release the GC reference; version floor stays behind.
            return true;
        }

        public void OnTimeout(object? argument)
            => entries.TryRemove(Key, this);
    }
}
