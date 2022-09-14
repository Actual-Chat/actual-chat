using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using ActualChat.UI.Blazor.Module;
using BlazorContextMenu;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.Components;

public class ContextMenuToggleTrigger : ContextMenuTrigger
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        /*
            <div @attributes="Attributes"
                 onclick="@($"blazorContextMenu.OnContextMenu(event, '{MenuId.Replace("'","\\'")}'); " : "")"
                 class="@CssClass">
                @ChildContent
            </div>
         */

        builder.OpenElement(0, WrapperTag);

        builder.AddMultipleAttributes(1,
            Microsoft.AspNetCore.Components.CompilerServices.RuntimeHelpers.TypeCheck<IEnumerable<KeyValuePair<string, object>>>(Attributes!));

        var triggerHandler = $"{BlazorUICoreModule.ImportName}.blazorContextMenu.OnContextMenuToggle(event, '{MenuId.OrdinalReplace("'", "\\'")}', {StopPropagation.ToString().ToLowerInvariant()});";
        builder.AddAttribute(2, "onclick", triggerHandler);

        if (!string.IsNullOrWhiteSpace(CssClass))
            builder.AddAttribute(5, "class", CssClass);
        builder.AddAttribute(6, "id", Id);
        builder.AddAttribute(7, "data-context-menu-toggle", true);
        builder.AddContent(8, ChildContent);
        builder.AddElementReferenceCapture(9, (value) => {
            ContextMenuTriggerElementRef = value;
        });
        builder.CloseElement();
    }
}
