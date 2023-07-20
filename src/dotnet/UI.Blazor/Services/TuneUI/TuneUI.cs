using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class TuneUI
{
    private IJSRuntime JS { get; }

    public TuneUI(IServiceProvider services)
        => JS = services.JSRuntime();

    public ValueTask Play(string tuneName, CancellationToken cancellationToken = default)
        => JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.TuneUI.play",
            cancellationToken,
            tuneName);

    public ValueTask PlayAndWait(string tuneName, CancellationToken cancellationToken = default)
        => JS.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.TuneUI.playAndWait",
            cancellationToken,
            tuneName);
}
