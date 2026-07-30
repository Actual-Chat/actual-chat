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
        public int PageSize { get; init; } = 32 * 1024;
        public long PageCacheBytes { get; init; } = 16 * 1024 * 1024;
        public TimeSpan FlushDelay { get; init; } = TimeSpan.FromSeconds(0.5);
    }

    public new Options Settings { get; }
    public KvasarKvas Kvas { get; }

    public KvasarRemoteComputedCache(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        Settings = settings;
        Kvas = new KvasarKvas(new KvasarKvas.Options() {
            BasePath = settings.BasePath,
            EncryptionKey = settings.EncryptionKey,
            Version = settings.Version,
            PageSize = settings.PageSize,
            PageCacheBytes = settings.PageCacheBytes,
            FlushDelay = settings.FlushDelay,
            // The whole cache is regenerable, so a power loss costs an upstream lookup, not correctness.
            Durability = KvasarDurability.Buffered,
        }, services);
        Store = Kvas;
        WhenInitialized = Kvas.WhenInitialized;
    }
}
