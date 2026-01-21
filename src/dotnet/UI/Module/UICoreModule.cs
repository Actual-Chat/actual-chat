using ActualChat.Hosting;
using ActualChat.UI.Services;

namespace ActualChat.UI.Module;

#pragma warning disable IL2026 // Fine for modules

public sealed class UICoreModule(IServiceProvider moduleServices)
    : HostModule<UISettings>(moduleServices)
{
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "Fine for Fusion.")]
    protected override void InjectServices(IServiceCollection services)
    {
        services.AddScoped(c => new ChunkedFileUploader(c));
        services.AddScoped(_ => new ChunkSizeSelectorRecommendation());
    }
}
