using ActualLab.IO;
using ActualLab.Kvasar;

namespace ActualChat.Kvas;

/// <summary>
/// An <see cref="IKvasStore"/> backed by an encrypted <see cref="KvasarStore"/>.
/// Degrades to a no-op store when it can't be opened - everything stored here is regenerable.
/// </summary>
public sealed class KvasarKvas : SafeAsyncDisposableBase, IKvasStore
{
    public record Options
    {
        public required FilePath BasePath { get; init; }
        public required byte[] EncryptionKey { get; init; }
        public string Version { get; init; } = "";
        public int PageSize { get; init; } = 16 * 1024;
        public long PageCacheBytes { get; init; } = 16 * 1024 * 1024;
        public TimeSpan FlushDelay { get; init; } = TimeSpan.FromSeconds(0.5);
        public KvasarDurability Durability { get; init; } = KvasarDurability.Buffered;
    }

    private static readonly Task<KvasarStore?> NoStoreTask = Task.FromResult<KvasarStore?>(null);

    private readonly Lock _lock = new();
    private Task<KvasarStore?> _storeTask;
    private bool _isSuspended;

    private ILogger Log { get; }

    public Options Settings { get; }
    public IServiceProvider Services { get; }
    public Task WhenInitialized { get; }

    public KvasarKvas(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        Log = services.LogFor(GetType());
        _storeTask = Open();
        WhenInitialized = _storeTask;
    }

    protected override Task DisposeAsync(bool disposing)
        => CloseStore(Detach());

    public async ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default)
    {
        var store = await GetStore().ConfigureAwait(false);
        if (store == null)
            return null;

        try {
            var value = await store.Get(key, cancellationToken).ConfigureAwait(false);
            return value?.ToArray();
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Get('{Key}') failed", key);
            return null;
        }
    }

    public async Task Set(string key, byte[]? value, CancellationToken cancellationToken = default)
    {
        var store = await GetStore().ConfigureAwait(false);
        if (store == null)
            return;

        try {
            await store.Set(key, ToValue(value), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Set('{Key}') failed", key);
        }
    }

    public async Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default)
    {
        if (items.Length == 0)
            return;

        var store = await GetStore().ConfigureAwait(false);
        if (store == null)
            return;

        var updates = new (KvasarKey Key, KvasarValue? Value)[items.Length];
        for (var i = 0; i < items.Length; i++) {
            var (key, value) = items[i];
            updates[i] = (key, ToValue(value));
        }
        try {
            await store.SetMany(updates, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "SetMany({Count} items) failed", items.Length);
        }
    }

    public async ValueTask<(string Key, byte[] Value)[]> ListAllEntries(CancellationToken cancellationToken = default)
    {
        var store = await GetStore().ConfigureAwait(false);
        if (store == null)
            return [];

        var result = new List<(string Key, byte[] Value)>();
        try {
            await foreach (var (key, value) in store.Scan(cancellationToken).ConfigureAwait(false))
                result.Add((key.AsString, value.ToArray()));
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "ListAllEntries failed after {Count} entries", result.Count);
        }
        return result.ToArray();
    }

    public async Task Flush(CancellationToken cancellationToken = default)
    {
        var store = await GetStore().ConfigureAwait(false);
        if (store == null)
            return;

        try {
            await store.Flush().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Flush failed");
        }
    }

    public async Task Clear(CancellationToken cancellationToken = default)
    {
        var store = await GetStore().ConfigureAwait(false);
        if (store == null)
            return;

        try {
            await store.Clear(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Clear failed");
        }
    }

    // Closes the store so no file handle or advisory lock survives into suspension - iOS kills an app
    // that holds one (0xdead10cc). Reads miss and writes are dropped until Resume reopens it.
    public Task Suspend()
    {
        Task<KvasarStore?> storeTask;
        lock (_lock) {
            if (_isSuspended)
                return Task.CompletedTask;

            _isSuspended = true;
            storeTask = _storeTask;
            Volatile.Write(ref _storeTask, NoStoreTask);
        }
        return CloseStore(storeTask);
    }

    public void Resume()
    {
        lock (_lock) {
            if (!_isSuspended || IsDisposed)
                return;

            _isSuspended = false;
            // Publication: readers pick the new task up with a Volatile.Read in GetStore.
            Volatile.Write(ref _storeTask, Open());
        }
    }

    // Private methods

    private Task<KvasarStore?> GetStore()
        // Volatile.Read pairs with the publication in Resume.
        => Volatile.Read(ref _storeTask);

    private async Task<KvasarStore?> Open()
    {
        var options = new KvasarOptions() {
            BasePath = Settings.BasePath,
            EncryptionKey = Settings.EncryptionKey,
            Version = Settings.Version,
            PageSize = Settings.PageSize,
            PageCacheBytes = Settings.PageCacheBytes,
            FlushDelay = Settings.FlushDelay,
            Durability = Settings.Durability,
        };
        try {
            return await KvasarStore.Open(options).ConfigureAwait(false);
        }
        catch (KvasarLockException e) {
            // Deleting the files wouldn't help: someone else is using them.
            Log.LogError(e, "Store '{BasePath}' is already open, it will be a no-op", Settings.BasePath);
            return null;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to open store '{BasePath}', deleting and retrying", Settings.BasePath);
            try {
                DeleteStoreFiles(Settings.BasePath);
                return await KvasarStore.Open(options).ConfigureAwait(false);
            }
            catch (Exception e2) {
                Log.LogError(e2, "Failed to open store '{BasePath}' after retry", Settings.BasePath);
                return null;
            }
        }
    }

    private Task<KvasarStore?> Detach()
    {
        lock (_lock) {
            var storeTask = _storeTask;
            Volatile.Write(ref _storeTask, NoStoreTask);
            return storeTask;
        }
    }

    private async Task CloseStore(Task<KvasarStore?> storeTask)
    {
        var store = (await storeTask.ResultAwait(false)).ValueOrDefault;
        if (store == null)
            return;

        try {
            await store.Flush().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Flush failed while closing the store");
        }
        try {
            await store.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "DisposeAsync failed while closing the store");
        }
    }

    private static void DeleteStoreFiles(FilePath basePath)
    {
        var directory = basePath.DirectoryPath;
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.EnumerateFiles(directory, basePath.FileName + ".*"))
            try {
                File.Delete(file);
            }
            catch (IOException) {
                // Intended: a leftover file just costs disk space, Open's retry decides the outcome
            }
    }

    private static KvasarValue? ToValue(byte[]? value)
        // A KvasarValue built from a null array is a *present, empty* value; a delete is a null KvasarValue.
        => value is null ? default(KvasarValue?) : new KvasarValue(value.AsMemory());
}
