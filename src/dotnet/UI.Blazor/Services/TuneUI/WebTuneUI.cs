using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TuneUI))]
[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TuneInfo))]
public class WebTuneUI(UIHub hub) : TuneUI(hub)
{
    private static readonly string JSPlayMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.play";
    private static readonly string JSPlayAndWaitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.playAndWait";

    private IJSRuntime JS => Hub.JS;
    private BrowserInfo BrowserInfo => Hub.BrowserInfo;

    protected override bool CanVibrate => BrowserInfo.CanVibrate;

    protected override Task PlayInternal(Tune tune)
        => ForegroundTask.Run(() => JS.InvokeVoidAsync(JSPlayMethod, tune).AsTask());

    protected override Task PlayAndWaitInternal(Tune tune)
        => JS.InvokeVoidAsync(JSPlayAndWaitMethod, tune).AsTask();

    protected override Task WhenCanVibrateKnown()
        => BrowserInfo.WhenReady;
}
