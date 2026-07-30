using ActualChat.Kvas;
using ActualChat.UI.Blazor.Module;
using ActualChat.UI.Caching;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Web browser implementation of remote computed cache using IndexedDB storage.
/// </summary>
public sealed class WebRemoteComputedCache : AppRemoteComputedCache
{
    public new record Options : AppRemoteComputedCache.Options
    {
        public BatchingKvas.Options Batching { get; init; } = new();
    }

    public new Options Settings { get; }

    public WebRemoteComputedCache(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        Settings = settings;
        Store = new BatchingKvas(settings.Batching, services) {
            Backend = new WebKvasBackend($"{BlazorUICoreModule.ImportName}.remoteComputedCache", services),
        }.Start();
    }
}
