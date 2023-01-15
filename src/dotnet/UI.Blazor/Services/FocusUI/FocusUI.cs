using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class FocusUI
{
    private IJSRuntime JS { get; }

    public FocusUI(IJSRuntime js)
        => JS = js;

    public ValueTask Focus(ElementReference targetRef)
        => JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.FocusUI.focus", targetRef);

    public ValueTask Blur()
        => JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.FocusUI.blur");
}
