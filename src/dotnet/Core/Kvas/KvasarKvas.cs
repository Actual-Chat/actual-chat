using ActualLab.IO;
using ActualLab.Kvasar;

namespace ActualChat.Kvas;

/// <summary>
/// An <see cref="IKvasStore"/> backed by an encrypted <see cref="KvasarStore"/>.
/// Degrades to a no-op store when it can't be opened - everything stored here is regenerable.
/// </summary>
public sealed class KvasarKvas : SafeAsyncDisposableBase, IKvasStore
{
    public sealed record Options
    {
        public required FilePath BasePath { get; init; }
        public required byte[] EncryptionKey { get; init; }
        public string Version { get; init; } = "";
        public int PageSize { get; init; } = 16 * 1024;
        public long PageCacheBytes { get; init; } = 16 * 1024 * 1024;
        public TimeSpan FlushDelay { get; init; } = TimeSpan.FromSeconds(0.5);
        public KvasarDurability Durability { get; init; } = KvasarDurability.Buffered;

        // Keyed stores live in "<BasePath>-<key>" folders and no-op until Activate picks the key
        public bool RequiresActivation { get; init; }
    }

    private static readonly Task<KvasarStore?> NoStoreTask = Task.FromResult<KvasarStore?>(null);

    private readonly Lock _lock = new();
    private Task<KvasarStore?> _storeTask;
    private string? _activeKey;
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
        _storeTask = settings.RequiresActivation ? NoStoreTask : Open(null);
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

    public Task Activate(string key)
    {
        // Points a keyed store at "<BasePath>-<key>", so what one key wrote can't be read back under
        // another - whether or not switching away manages to delete the old folder.
        ArgumentException.ThrowIfNullOrEmpty(key);

        Task<KvasarStore?> oldStoreTask;
        string? oldKey;
        Task<KvasarStore?> newStoreTask;
        lock (_lock) {
            if (IsDisposed)
                return Task.CompletedTask;
            if (_activeKey == key)
                return _storeTask;

            oldStoreTask = _storeTask;
            oldKey = _activeKey;
            _activeKey = key;
            // Publication: readers pick the new task up with a Volatile.Read in GetStore.
            newStoreTask = _isSuspended ? NoStoreTask : Open(key);
            Volatile.Write(ref _storeTask, newStoreTask);
        }

        _ = SwitchAway(oldStoreTask, oldKey, key);
        return newStoreTask;
    }

    public Task Deactivate(bool clear)
    {
        Task<KvasarStore?> oldStoreTask;
        string? oldKey;
        lock (_lock) {
            oldStoreTask = _storeTask;
            oldKey = _activeKey;
            _activeKey = null;
            // Publication: readers pick the new task up with a Volatile.Read in GetStore.
            Volatile.Write(ref _storeTask, NoStoreTask);
        }

        return CloseAndDelete(oldStoreTask, clear ? oldKey : null);
    }

    public Task Suspend()
    {
        // Closes the store so no file handle or advisory lock survives into suspension - iOS kills an
        // app that holds one (0xdead10cc). Reads miss and writes are dropped until Resume reopens it.
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
            if (Settings.RequiresActivation && _activeKey == null)
                return;

            // Publication: readers pick the new task up with a Volatile.Read in GetStore.
            Volatile.Write(ref _storeTask, Open(_activeKey));
        }
    }

    // Private methods

    private Task<KvasarStore?> GetStore()
        // Volatile.Read pairs with the publication in Resume.
        => Volatile.Read(ref _storeTask);

    private Task<KvasarStore?> Open(string? key)
        // Deferred to the pool: callers hold _lock, and both the directory creation and Kvasar's own
        // open path are synchronous file I/O - Activate is on the session switch path.
        => Task.Run(() => OpenStore(key));

    private async Task<KvasarStore?> OpenStore(string? key)
    {
        var basePath = GetBasePath(key);
        var options = new KvasarOptions {
            BasePath = basePath,
            EncryptionKey = Settings.EncryptionKey,
            Version = Settings.Version,
            PageSize = Settings.PageSize,
            PageCacheBytes = Settings.PageCacheBytes,
            FlushDelay = Settings.FlushDelay,
            Durability = Settings.Durability,
        };
        try {
            Directory.CreateDirectory(basePath.DirectoryPath);
            return await KvasarStore.Open(options).ConfigureAwait(false);
        }
        catch (KvasarLockException e) {
            // Deleting the files wouldn't help: someone else is using them.
            Log.LogError(e, "Store '{BasePath}' is already open, it will be a no-op", basePath);
            return null;
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to open store '{BasePath}', deleting and retrying", basePath);
            try {
                DeleteStoreFiles(basePath);
                Directory.CreateDirectory(basePath.DirectoryPath);
                return await KvasarStore.Open(options).ConfigureAwait(false);
            }
            catch (Exception e2) {
                Log.LogError(e2, "Failed to open store '{BasePath}' after retry", basePath);
                return null;
            }
        }
    }

    private FilePath GetBasePath(string? key)
        => key == null
            ? Settings.BasePath
            : Settings.BasePath.DirectoryPath
                & (Settings.BasePath.FileName + "-" + key)
                & Settings.BasePath.FileName;

    private async Task SwitchAway(Task<KvasarStore?> oldStoreTask, string? oldKey, string newKey)
    {
        await CloseAndDelete(oldStoreTask, oldKey).ConfigureAwait(false);
        SweepKeyFolders(newKey);
    }

    private async Task CloseAndDelete(Task<KvasarStore?> storeTask, string? keyToDelete)
    {
        // The store holds an advisory lock file, so it has to be closed before its folder can go.
        await CloseStore(storeTask).ConfigureAwait(false);
        if (keyToDelete != null)
            DeleteKeyFolder(keyToDelete);
    }

    private void DeleteKeyFolder(string key)
    {
        var folder = GetBasePath(key).DirectoryPath;
        try {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to delete store folder '{Folder}'", folder);
        }
    }

    private void SweepKeyFolders(string keyToKeep)
    {
        var root = Settings.BasePath.DirectoryPath;
        var prefix = Settings.BasePath.FileName + "-";
        var folderToKeep = prefix + keyToKeep;
        try {
            if (!Directory.Exists(root))
                return;

            foreach (var folder in Directory.EnumerateDirectories(root, prefix + "*")) {
                var folderName = ((FilePath)folder).FileName;
                if (folderName == folderToKeep)
                    continue;

                try {
                    Directory.Delete(folder, true);
                    Log.LogInformation("Swept stale store folder '{Folder}'", folder);
                }
                catch (Exception e) {
                    Log.LogWarning(e, "Failed to sweep store folder '{Folder}'", folder);
                }
            }
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to sweep stale store folders in '{Root}'", root);
        }
    }

    private Task<KvasarStore?> Detach()
    {
        lock (_lock) {
            var storeTask = _storeTask;
            _activeKey = null;
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
