namespace ActualChat.UI.Blazor.Services;

public abstract class Temporals(UIHub hub) : UIServiceBase<UIHub>(hub), IComputeService
{
    public readonly TimeSpan DefaultExpiresIn = TimeSpan.FromSeconds(3);

    protected readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.Ordinal);

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

    protected abstract class Entry
    {
        public Temporals Host { get; }
        public string Key { get; set; }
        public abstract object? UntypedValue { get; }
        public TimeSpan ExpiresIn { get; }
        public Task? ExpireTask { get; private set; }

        protected Entry(Temporals host, string key, TimeSpan expiresIn)
        {
            Host = host;
            Key = key;
            ExpiresIn = expiresIn;
        }

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
            if (!Host.Entries.TryRemove(KeyValuePair.Create(Key, this)))
                return;

            using (Invalidation.Begin())
                _ = Host.GetEntry(Key);
        }
    }

    protected sealed class Entry<T>(Temporals host, string key, T value, TimeSpan expiresIn)
        : Entry(host, key, expiresIn)
    {
        public T Value { get; set; } = value;
        public override object? UntypedValue => Value;
    }
}
