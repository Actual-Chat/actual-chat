namespace ActualChat.Kvas;

/// <summary>
/// Local key-value store for client-side settings persistence.
/// The actual storage is provided by <see cref="Options.StoreFactory"/>.
/// </summary>
public sealed class LocalSettings : SafeAsyncDisposableBase, IKvasStore
{
    public record Options
    {
        public required Func<IServiceProvider, IKvasStore> StoreFactory { get; init; }
    }

    public Options Settings { get; }
    public IKvasStore Store { get; }
    public IServiceProvider Services { get; }

    public LocalSettings(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        Store = settings.StoreFactory.Invoke(services);
    }

    protected override async Task DisposeAsync(bool disposing)
    {
        if (Store is IAsyncDisposable disposable)
            await disposable.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default)
        => Store.Get(key, cancellationToken);

    public Task Set(string key, byte[]? value, CancellationToken cancellationToken = default)
        => Store.Set(key, value, cancellationToken);

    public Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default)
        => Store.SetMany(items, cancellationToken);

    public ValueTask<(string Key, byte[] Value)[]> ListAllEntries(CancellationToken cancellationToken = default)
        => Store.ListAllEntries(cancellationToken);

    public Task Flush(CancellationToken cancellationToken = default)
        => Store.Flush(cancellationToken);

    public Task Clear(CancellationToken cancellationToken = default)
        => Store.Clear(cancellationToken);
}
