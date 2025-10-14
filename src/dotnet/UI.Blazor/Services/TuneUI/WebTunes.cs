using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TuneUI))]
[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TuneInfo))]
public class WebTunes(UIHub hub) : TuneUI(hub)
{
    private static readonly string JSPlayMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.play";
    private static readonly string JSPlayAndWaitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.playAndWait";

    private IJSRuntime JS => Hub.JS;

    public override Task Play(Tune tune, CancellationToken cancellationToken = default)
        => ForegroundTask.Run(() => JS.InvokeVoidAsync(JSPlayMethod, cancellationToken, tune).AsTask(), cancellationToken);

    public override Task PlayAndWait(Tune tune, CancellationToken cancellationToken = default)
        => JS.InvokeVoidAsync(JSPlayAndWaitMethod, cancellationToken, tune).AsTask();
}

