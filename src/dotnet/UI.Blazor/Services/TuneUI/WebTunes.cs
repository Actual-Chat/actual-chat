using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

// Web-specific implementation with JS interop moved from base TuneUI
public class WebTunes : TuneUI
{
    private static readonly string JSInitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.init";
    private static readonly string JSPlayMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.play";
    private static readonly string JSPlayAndWaitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.playAndWait";

    private IJSRuntime JS => Hub.JS;

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TuneUI))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TuneInfo))]
    public WebTunes(UIHub hub) : base(hub)
        => _ = Initialize();

    private async ValueTask Initialize()
    {
        try {
            await JS.InvokeVoidAsync(JSInitMethod, Tunes).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Initialize failed");
        }
    }

    public override Task Play(Tune tune, CancellationToken cancellationToken = default)
        => ForegroundTask.Run(() => JS.InvokeVoidAsync(JSPlayMethod, cancellationToken, tune).AsTask(), cancellationToken);

    public override Task PlayAndWait(Tune tune, CancellationToken cancellationToken = default)
        => JS.InvokeVoidAsync(JSPlayAndWaitMethod, cancellationToken, tune).AsTask();
}

