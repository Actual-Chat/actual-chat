using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class KeepAwakeUI
{
    private readonly IJSRuntime _js;

    public KeepAwakeUI(IJSRuntime js)
        => _js = js;

    public ValueTask SetKeepAwake(bool value, CancellationToken cancellationToken)
        => _js.InvokeVoidAsync(
            $"{BlazorUICoreModule.ImportName}.KeepAwakeUI.setKeepAwake",
            cancellationToken,
            value);
}
