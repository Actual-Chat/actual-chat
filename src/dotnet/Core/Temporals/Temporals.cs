namespace ActualChat;

/// <summary>
/// Provides short-lived in-memory overrides for values.
/// When a value is set, it's immediately available locally and expires after a timeout.
/// Server side uses <see cref="FakeTemporals"/> (no-op), client side uses <see cref="RealTemporals"/>.
/// </summary>
public abstract class Temporals : IComputeService, IDisposable, IHasDisposeStatus
{
    public static readonly TimeSpan DefaultExpiresIn = TimeSpan.FromSeconds(3);

    private volatile int _isDisposed;

    protected readonly ConcurrentDictionary<string, Entry> Entries = new();

    public bool IsReal { get; protected init; }
    public bool IsDisposed => _isDisposed != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    { }

    public async ValueTask<T?> Get<T>(string key)
        where T : notnull
    {
        var entry = await GetEntry(key).ConfigureAwait(false);
        return entry is null ? default : ((Entry<T>)entry).Value;
    }

    public void Set<T>(string key, T value)
        where T : notnull
        => SetEntry(key, value, DefaultExpiresIn);

    public void Set<T>(string key, T value, TimeSpan expiresIn)
        where T : notnull
        => SetEntry(key, value, expiresIn);

    public void Remove(string key)
        => RemoveEntry(key);

    // Protected methods

    protected abstract ValueTask<Entry?> GetEntry(string key);
    protected abstract void SetEntry<T>(string key, T value, TimeSpan expiresIn);
    protected abstract void RemoveEntry(string key);

    // Nested types

    protected abstract class Entry(Temporals host, string key, TimeSpan expiresIn)
    {
        public Temporals Host { get; } = host;
        public string Key { get; init; } = key;
        public abstract object? UntypedValue { get; }
        public TimeSpan ExpiresIn { get; } = expiresIn;
        public Task? ExpireTask { get; private set; }

        public void StartExpiration()
        {
            if (ExpiresIn <= TimeSpan.Zero) {
                ExpireTask = Task.CompletedTask;
                Host.Entries.GetValueOrDefault(Key)?.EndExpiration();
                return;
            }

            Host.Entries[Key] = this;
            ExpireTask = Task
                .Delay(ExpiresIn)
                .ContinueWith(_ => EndExpiration(), TaskScheduler.Default);
            using (Invalidation.Begin())
                _ = Host.GetEntry(Key);
        }

        public void EndExpiration()
        {
            if (!Host.Entries.TryRemove(Key, this))
                return;

            using (Invalidation.Begin())
                _ = Host.GetEntry(Key);
        }
    }

    protected sealed class Entry<T>(Temporals host, string key, T value, TimeSpan expiresIn)
        : Entry(host, key, expiresIn)
    {
        public T Value { get; } = value;
        public override object? UntypedValue => Value;
    }
}
