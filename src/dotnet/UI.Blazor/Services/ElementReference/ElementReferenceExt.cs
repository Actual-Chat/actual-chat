using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public static class ElementReferenceExt
{
    public static readonly string JSScrollToTopMethod = $"{BlazorUICoreModule.ImportName}.ElementUtils.scrollToTop";

    public static ValueTask ScrollToTop(this ElementReference elementReference)
        => DemandJSRuntime(elementReference).InvokeVoidAsync(JSScrollToTopMethod, elementReference);

    private static IJSRuntime DemandJSRuntime(ElementReference elementReference)
        => elementReference.GetJSRuntime() ?? throw new InvalidOperationException("No JavaScript runtime found.");

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<JSRuntime>k__BackingField")]
    private static extern ref IJSRuntime GetJSRuntime(WebElementReferenceContext webElementReference);

    internal static IJSRuntime GetJSRuntime(this ElementReference elementReference)
    {
        // This method is a copy from Microsoft.AspNetCore.Components.ElementReferenceExtensions.GetJSRuntime method.
        if (!(elementReference.Context is WebElementReferenceContext context))
            throw new InvalidOperationException("ElementReference has not been configured correctly.");

        return GetJSRuntime(context);
    }
}
