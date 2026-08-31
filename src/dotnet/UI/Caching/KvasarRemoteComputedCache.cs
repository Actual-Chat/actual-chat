using ActualChat.Kvas;
using ActualLab.IO;
using ActualLab.Kvasar;

namespace ActualChat.UI.Caching;

/// <summary>
/// Remote computed cache backed by an encrypted <see cref="KvasarStore"/>.
/// It talks to the store directly: Kvasar batches writes and caches pages itself.
/// </summary>
public sealed class KvasarRemoteComputedCache : AppRemoteComputedCache
{
    public new record Options : AppRemoteComputedCache.Options
    {
        public required FilePath BasePath { get; init; }
        public required byte[] EncryptionKey { get; init; }
        // 32 KiB is the best page size per Kvasar's benchmarks, Android is a deliberate exception
        public int PageSize { get; init; } = (OSInfo.IsAndroid ? 16 : 32) * 1024;
        public long PageCacheBytes { get; init; } = 8 * 1024 * 1024;
        public TimeSpan FlushDelay { get; init; } = TimeSpan.FromSeconds(0.667);
    }

    private readonly TaskCompletionSource _whenActivated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public new Options Settings { get; }
    public KvasarKvas Kvas { get; }

    public KvasarRemoteComputedCache(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        Settings = settings;
        Kvas = new KvasarKvas(new KvasarKvas.Options {
            BasePath = settings.BasePath,
            EncryptionKey = settings.EncryptionKey,
            Version = settings.Version,
            PageSize = settings.PageSize,
            PageCacheBytes = settings.PageCacheBytes,
            FlushDelay = settings.FlushDelay,
            // The whole cache is regenerable, so a power loss costs an upstream lookup, not correctness.
            Durability = KvasarDurability.Buffered,
            // Cache keys carry Session.Default rather than the real id, so entries are only
            // distinguishable by the folder they live in - one per session, picked by Activate.
            RequiresActivation = true,
        }, services);
        Store = Kvas;
        // Not Kvas.WhenInitialized: a RequiresActivation store reports that completed from its
        // constructor, so it says nothing about whether there's a folder to read from yet.
        WhenInitialized = _whenActivated.Task;
    }

    public async Task Activate(Session session)
    {
        try {
            await Kvas.Activate(session.Hash).ConfigureAwait(false);
            _whenActivated.TrySetResult();
        }
        catch (Exception e) {
            // Faulted, never left pending: a pending WhenInitialized would silently turn every
            // Get into a miss and every Set into a no-op for the rest of the process.
            Log.LogError(e, "Activate failed");
            _whenActivated.TrySetException(e);
            _ = _whenActivated.Task.Exception; // Nothing awaits WhenInitialized, so observe it here
            throw;
        }
    }

    public Task Deactivate(bool clear)
        => Kvas.Deactivate(clear);
}
