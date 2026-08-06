using ActualChat.UI.Services;

namespace ActualChat.UI.Module;

public sealed class UICoreModule(IServiceProvider moduleServices)
    : HostModule<UISettings>(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        services.AddScoped(c => new ChunkedFileUploader(c));
        services.AddScoped(_ => new ChunkSizeSelectorRecommendation());
    }
}
