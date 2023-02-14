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

    public async Task Init()
    {
        try {
            _backendRef = DotNetObjectReference.Create<IAudioInfoBackend>(this);
            await JS.InvokeVoidAsync(
                $"{AudioBlazorUIModule.ImportName}.AudioInfo.init",
                _backendRef);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Failed to initialize audio pipeline");
            throw;
        }

    }

    public void Dispose()
        => _backendRef.DisposeSilently();
}
