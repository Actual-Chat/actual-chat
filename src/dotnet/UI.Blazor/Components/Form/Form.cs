using System.Reflection;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.Components;

public class Form : EditForm
{
    private readonly Func<Task> _handleSubmitCached;

    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool IsHorizontal { get; set; }

    public Form()
        // The same private field declared in the base class, we just need to pull its value here
        => _handleSubmitCached = (Func<Task>)typeof(EditForm)
            .GetField("_handleSubmitDelegate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(this)!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var editContext = EditContext;
        Debug.Assert(editContext != null);

        // If _editContext changes, tear down and recreate all descendants.
        // This is so we can safely use the IsFixed optimization on CascadingValue,
        // optimizing for the common case where _editContext never changes.
        builder.OpenRegion(editContext.GetHashCode());

        var i = 0;
        builder.OpenElement(i++, "form");
        builder.AddAttribute(i++, "class", $"form {(IsHorizontal ? "form-x" : "form-y")}");
        builder.AddMultipleAttributes(i++, AdditionalAttributes);
        builder.AddAttribute(i++, "onsubmit", _handleSubmitCached);
        builder.OpenComponent<CascadingValue<EditContext>>(i++);
        builder.AddAttribute(i++, "IsFixed", true);
        builder.AddAttribute(i++, "Value", editContext);
        builder.AddAttribute(i++, "ChildContent", ChildContent?.Invoke(editContext));
        builder.CloseComponent();
        builder.CloseElement();

        builder.CloseRegion();
    }
}
