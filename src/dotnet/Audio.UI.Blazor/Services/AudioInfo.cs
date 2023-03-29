using ActualChat.Audio.UI.Blazor.Module;

namespace ActualChat.Audio.UI.Blazor.Services;

public sealed class AudioInfo : IAudioInfoBackend, IDisposable
{
    private DotNetObjectReference<IAudioInfoBackend>? _backendRef;

    private IServiceProvider Services { get; }
    private ILogger Log { get; }
    private IJSRuntime JS { get; }
    private UrlMapper UrlMapper { get; }

    public AudioInfo(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        JS = services.GetRequiredService<IJSRuntime>();
        UrlMapper = services.GetRequiredService<UrlMapper>();
    }

    public void Dispose()
        => _backendRef.DisposeSilently();

    public Task Initialize()
        => new AsyncChain(nameof(Initialize), async ct => {
                var backendRef = _backendRef ??= DotNetObjectReference.Create<IAudioInfoBackend>(this);
                await JS.InvokeVoidAsync(
                    $"{AudioBlazorUIModule.ImportName}.AudioInfo.init",
                    ct,
                    backendRef);
            })
            .Log(Log)
            .RetryForever(new RetryDelaySeq(0.5, 3), Log)
            .RunIsolated();
}
