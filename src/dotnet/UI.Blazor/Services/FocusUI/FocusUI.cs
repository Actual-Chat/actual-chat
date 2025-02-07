using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class FocusUI(UIHub hub)
{
    private static readonly string JSFocusMethod = $"{BlazorUICoreModule.ImportName}.FocusUI.focus";
    private static readonly string JSBlurMethod = $"{BlazorUICoreModule.ImportName}.FocusUI.blur";

    private IJSRuntime JS => hub.JSRuntime();

    public ValueTask Focus(ElementReference targetRef)
        => JS.InvokeVoidAsync(JSFocusMethod, targetRef);

    public ValueTask Blur()
        => JS.InvokeVoidAsync(JSBlurMethod);
}
