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

        builder.AddMultipleAttributes(1, Microsoft.AspNetCore.Components.CompilerServices.RuntimeHelpers.TypeCheck<global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>>>(Attributes));

        builder.AddAttribute(2, "onclick", $"blazorContextMenu.OnContextMenuToggle(event, '{MenuId.Replace("'", "\\'")}', {StopPropagation.ToString().ToLower()});");

        if (!string.IsNullOrWhiteSpace(CssClass))
        {
            builder.AddAttribute(5, "class", CssClass);
        }
        builder.AddAttribute(6, "id", Id);
        builder.AddContent(7, ChildContent);
        builder.AddElementReferenceCapture(8, (__value) =>
        {
            SetContextMenuTriggerElementRef(__value);
        });
        builder.CloseElement();
    }

    private void SetContextMenuTriggerElementRef(ElementReference elementReference)
        => _setElementRefAction.Invoke(this, elementReference);
}
