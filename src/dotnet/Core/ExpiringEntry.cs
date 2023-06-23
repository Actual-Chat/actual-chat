namespace ActualChat;

public static class ExpiringEntry
{
    public static ExpiringEntry<TKey, TValue> New<TKey, TValue>(
        ConcurrentDictionary<TKey, ExpiringEntry<TKey, TValue>> dictionary,
        TKey key, TValue value,
        CancellationTokenSource? disposeTokenSource = null)
        where TKey : notnull
        => new(dictionary, key, value, disposeTokenSource);
}

public sealed class ExpiringEntry<TKey, TValue> : IDisposable
    where TKey : notnull
{
    private CancellationTokenSource? _disposeTokenSource;
    private readonly CancellationToken _disposeToken;
    private Func<ExpiringEntry<TKey, TValue>, ValueTask>? _disposer;
    private long _expiredAt;

    public ConcurrentDictionary<TKey, ExpiringEntry<TKey, TValue>> Dictionary { get; }
    public TKey Key { get; }
    public TValue Value { get; }
    public IMomentClock Clock { get; private set; }

    public Moment ExpiredAt {
        get {
            var expiredAt = Interlocked.Read(ref _expiredAt);
            return new Moment(expiredAt);
        }
        private set => Interlocked.Exchange(ref _expiredAt, value.EpochOffsetTicks);
    }

    public bool IsDisposed => _disposeToken.IsCancellationRequested;

    public ExpiringEntry(
        ConcurrentDictionary<TKey, ExpiringEntry<TKey, TValue>> dictionary,
        TKey key, TValue value,
        CancellationTokenSource? disposeTokenSource = null)
    {
        Key = key;
        Value = value;
        Dictionary = dictionary;
        Clock = MomentClockSet.Default.CpuClock;
        _disposeTokenSource = disposeTokenSource ?? new CancellationTokenSource();
        _disposeToken = _disposeTokenSource.Token;
    }

    public void Dispose()
    {
        var cts = Interlocked.Exchange(ref _disposeTokenSource, null);
        if (cts == null)
            return;

        Dictionary.TryRemove(KeyValuePair.Create(Key, this));
        cts.CancelAndDisposeSilently();
        if (_disposer != null)
            _ = _disposer.Invoke(this);
        else if (Value is IAsyncDisposable ad)
            _ = ad.DisposeAsync();
        else if (Value is IDisposable d)
            d.Dispose();
    }

    public void Deconstruct(out TKey key, out TValue value)
    {
        key = Key;
        value = Value;
    }

    public override string ToString()
        => $"{GetType().GetName()}({Key}, {Value})";

    public ExpiringEntry<TKey, TValue> SetDisposer(Func<ExpiringEntry<TKey, TValue>, ValueTask> disposer)
    {
        _disposer = disposer;
        return this;
    }

    public ExpiringEntry<TKey, TValue> SetDisposer(Action<ExpiringEntry<TKey, TValue>> disposer)
    {
        _disposer = x => {
            disposer.Invoke(x);
            return default;
        };
        return this;
    }

    public ExpiringEntry<TKey, TValue> BumpExpiresAt(Moment expiresAt, IMomentClock? clock = null)
    {
        if (clock != null)
            Clock = clock;
        ExpiredAt = Moment.Max(ExpiredAt, expiresAt);
        return this;
    }

    public ExpiringEntry<TKey, TValue> BumpExpiresAt(TimeSpan expiresIn, IMomentClock? clock = null)
    {
        if (clock != null)
            Clock = clock;
        ExpiredAt = Moment.Max(ExpiredAt, Clock.Now + expiresIn);
        return this;
    }

    public ExpiringEntry<TKey, TValue> BeginExpire()
    {
        _ = Expire();
        return this;
    }

    public Task Expire()
    {
        if (ExpiredAt == default)
            throw StandardError.StateTransition("ExpiresAt is not set.");
        return BackgroundTask.Run(async () => {
            try {
                var expiredAt = ExpiredAt;
                while (true) {
                    await Clock.Delay(expiredAt, _disposeToken).ConfigureAwait(false);
                    expiredAt = ExpiredAt;
                    if (expiredAt <= Clock.Now)
                        return;
                }
            }
            finally {
                Dispose();
            }
        });
    }
}
