using Microsoft.AspNetCore.Components;

namespace ActualChat.UI.Blazor;

public static class ComponentExt
{
    public static string DefaultClass(this IComponent component)
        => CssClasses.Default[component.GetType()];
}
