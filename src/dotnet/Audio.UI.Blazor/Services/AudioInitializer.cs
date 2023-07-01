using ActualChat.Audio.UI.Blazor.Module;

namespace ActualChat.Audio.UI.Blazor.Services;

public sealed class AudioInitializer : IAudioInfoBackend, IDisposable
{
    private DotNetObjectReference<IAudioInfoBackend>? _backendRef;

    private IServiceProvider Services { get; }
    private ILogger Log { get; }
    private IJSRuntime JS { get; }
    private UrlMapper UrlMapper { get; }

    public Task WhenInitialized { get; }

    public AudioInitializer(IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());

        JS = services.GetRequiredService<IJSRuntime>();
        UrlMapper = services.GetRequiredService<UrlMapper>();
        WhenInitialized = Initialize();
    }

    public void Dispose()
        => _backendRef.DisposeSilently();

    private Task Initialize()
        => new AsyncChain(nameof(Initialize), async ct => {
                var backendRef = _backendRef ??= DotNetObjectReference.Create<IAudioInfoBackend>(this);
                await JS.InvokeVoidAsync($"{AudioBlazorUIModule.ImportName}.AudioInitializer.init",
                    ct,
                    backendRef,
                    UrlMapper.BaseUrl);
            })
            .Log(Log)
            .RetryForever(RetryDelaySeq.Exp(0.5, 3), Log)
            .RunIsolated();
}
