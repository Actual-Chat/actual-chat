using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BlazorContextMenu;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.Components;

public class ContextMenuToggleTrigger : ContextMenuTrigger
{
    private static readonly Action<ContextMenuTrigger, ElementReference?> _setElementRefAction;

    static ContextMenuToggleTrigger()
    {
        var fieldInfo = typeof(ContextMenuTrigger)
            .GetField("contextMenuTriggerElementRef", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var triggerInstance = Expression.Parameter(typeof(ContextMenuTrigger));
        var elementReference = Expression.Parameter(typeof(ElementReference?));
        var body = Expression.Assign(Expression.Field(triggerInstance, fieldInfo), elementReference);
        var lambda = Expression.Lambda<Action<ContextMenuTrigger, ElementReference?>>(body, triggerInstance, elementReference);
        _setElementRefAction = lambda.Compile();
    }

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
            Microsoft.AspNetCore.Components.CompilerServices.RuntimeHelpers.TypeCheck<IEnumerable<KeyValuePair<string, object>>>(Attributes));

        builder.AddAttribute(2, "onclick",
            $"blazorContextMenu.OnContextMenuToggle(event, '{MenuId.OrdinalReplace("'", "\\'")}', {StopPropagation.ToString().ToLowerInvariant()});");

        if (!string.IsNullOrWhiteSpace(CssClass))
            builder.AddAttribute(5, "class", CssClass);
        builder.AddAttribute(6, "id", Id);
        builder.AddAttribute(7, "data-context-menu-toggle", true);
        builder.AddContent(8, ChildContent);
        builder.AddElementReferenceCapture(9, SetContextMenuTriggerElementRef);
        builder.CloseElement();
    }

    private void SetContextMenuTriggerElementRef(ElementReference elementReference)
        => _setElementRefAction.Invoke(this, elementReference);
}
