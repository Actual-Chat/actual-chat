namespace ActualChat.Commands;

/// <summary>
/// In-process store of <see cref="ApiCommand"/> results: the first caller of a key claims it and runs the
/// command, duplicates replay its result or await it. Entries never leave this process, so a duplicate
/// that lands on another node runs the command again.
/// </summary>
public sealed class IdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyEntry> _entries = new();
    private long _nextPruneAt;

    // Guards the live-but-slow case only: a claim that outlives this TTL is dropped, so a duplicate
    // re-runs the command. Must comfortably exceed the slowest realistic command.
    public TimeSpan InProgressTtl { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan CompletedTtl { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan PruneInterval { get; init; } = TimeSpan.FromMinutes(1);
    // Hard cap on resident entries: past it the oldest claims are dropped, i.e. the dedup window shortens
    public int MaxEntryCount { get; init; } = 100_000;
    public int EntryCount => _entries.Count;

    public bool TryClaim(string key, out IdempotencyEntry entry)
    {
        PruneIfDue();
        while (true) {
            if (_entries.TryGetValue(key, out var existing)) {
                if (!existing.IsExpired) {
                    entry = existing;
                    return false;
                }

                Drop(existing);
                continue;
            }

            var claim = new IdempotencyEntry(this, key, CpuTimestamp.Now + InProgressTtl);
            if (_entries.TryAdd(key, claim)) {
                entry = claim;
                return true;
            }
        }
    }

    // Internal methods

    internal void Drop(IdempotencyEntry entry)
    {
        _entries.TryRemove(KeyValuePair.Create(entry.Key, entry));
        entry.OnDropped();
    }

    // Private methods

    private void PruneIfDue()
    {
        var now = CpuTimestamp.Now;
        var nextPruneAt = Interlocked.Read(ref _nextPruneAt);
        if (now.Value < nextPruneAt)
            return;
        if (Interlocked.CompareExchange(ref _nextPruneAt, (now + PruneInterval).Value, nextPruneAt) != nextPruneAt)
            return;

        Prune();
    }

    private void Prune()
    {
        foreach (var entry in _entries.Values)
            if (entry.IsExpired)
                Drop(entry);

        var extraCount = _entries.Count - MaxEntryCount;
        if (extraCount <= 0)
            return;

        foreach (var entry in _entries.Values.OrderBy(x => x.ExpiresAt.Value).Take(extraCount))
            Drop(entry);
    }
}

/// <summary>
/// A single claim in <see cref="IdempotencyStore"/>. Its owner runs the command and calls
/// <see cref="Complete"/> or <see cref="Release"/>; duplicates read <see cref="Result"/> or await
/// <see cref="WhenCompleted"/>, which yields <c>null</c> when the claim is dropped without a result.
/// </summary>
public sealed class IdempotencyEntry
{
    private readonly TaskCompletionSource<ReadOnlyMemory<byte>?> _whenCompletedSource
        = TaskCompletionSourceExt.New<ReadOnlyMemory<byte>?>();
    private long _expiresAt;

    private IdempotencyStore Store { get; }

    internal string Key { get; }

    public Task<ReadOnlyMemory<byte>?> WhenCompleted => _whenCompletedSource.Task;
    public ReadOnlyMemory<byte>? Result
        => _whenCompletedSource.Task is { IsCompletedSuccessfully: true } whenCompleted
            ? whenCompleted.Result
            : null;
    public CpuTimestamp ExpiresAt {
        get => new(Interlocked.Read(ref _expiresAt));
        private set => Interlocked.Exchange(ref _expiresAt, value.Value);
    }
    public bool IsExpired => ExpiresAt.Elapsed >= TimeSpan.Zero;

    internal IdempotencyEntry(IdempotencyStore store, string key, CpuTimestamp expiresAt)
    {
        Store = store;
        Key = key;
        ExpiresAt = expiresAt;
    }

    public void Complete(ReadOnlyMemory<byte> result)
    {
        // Extends the entry's life before publishing the result, so a concurrent prune can't drop it as expired
        ExpiresAt = CpuTimestamp.Now + Store.CompletedTtl;
        _whenCompletedSource.TrySetResult(result);
    }

    public void Release()
        => Store.Drop(this);

    // Internal methods

    internal void OnDropped()
        => _whenCompletedSource.TrySetResult(null);
}
