using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class WebRemoteComputedCache : AppRemoteComputedCache
{
    public new record Options : AppRemoteComputedCache.Options;

    public new Options Settings { get; }

    public WebRemoteComputedCache(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        Settings = settings;
        Backend = new WebKvasBackend($"{BlazorUICoreModule.ImportName}.remoteComputedCache", services);
        _ = Reader.Start();
    }
}
