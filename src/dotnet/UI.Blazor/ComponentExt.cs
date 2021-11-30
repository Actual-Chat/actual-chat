using Microsoft.AspNetCore.Components;

namespace ActualChat.UI.Blazor;

public static class ComponentExt
{
    public static string ComponentCssClass(this IComponent component)
        => CssClassRegistry.Get(component.GetType());
}
