using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class FocusUI
{
    private static readonly string JSFocusMethod = $"{BlazorUICoreModule.ImportName}.FocusUI.focus";
    private static readonly string JSBlurMethod = $"{BlazorUICoreModule.ImportName}.FocusUI.blur";

    private IJSRuntime JS { get; }

    public FocusUI(IJSRuntime js)
        => JS = js;

    public ValueTask Focus(ElementReference targetRef)
        => JS.InvokeVoidAsync(JSFocusMethod, targetRef);

    public ValueTask Blur()
        => JS.InvokeVoidAsync(JSBlurMethod);
}
