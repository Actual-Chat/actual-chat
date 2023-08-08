using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class TuneUI
{
    private static readonly string JSPlayMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.play";
    private static readonly string JSPlayAndWaitMethod = $"{BlazorUICoreModule.ImportName}.TuneUI.playAndWait";

    private IJSRuntime JS { get; }

    public TuneUI(IServiceProvider services)
        => JS = services.JSRuntime();

    public ValueTask Play(string tuneName, CancellationToken cancellationToken = default)
        => JS.InvokeVoidAsync(JSPlayMethod, cancellationToken, tuneName);

    public ValueTask PlayAndWait(string tuneName, CancellationToken cancellationToken = default)
        => JS.InvokeVoidAsync(JSPlayAndWaitMethod, cancellationToken, tuneName);
}
