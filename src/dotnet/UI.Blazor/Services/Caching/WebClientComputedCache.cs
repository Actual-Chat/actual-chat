using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class WebClientComputedCache : AppClientComputedCache
{
    public new record Options : AppClientComputedCache.Options;

    public new Options Settings { get; }

    public WebClientComputedCache(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        Settings = settings;
        Backend = new WebKvasBackend($"{BlazorUICoreModule.ImportName}.clientComputedCache", services);
    }
}
