using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class KeepAwakeUI
{
    private IJSRuntime JS { get; }

    public KeepAwakeUI(IJSRuntime js)
        => JS = js;

    public ValueTask SetKeepAwake(bool value)
        => JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.KeepAwakeUI.setKeepAwake", value);
}
