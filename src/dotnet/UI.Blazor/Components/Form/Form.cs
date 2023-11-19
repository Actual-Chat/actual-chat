using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.Components;

public class Form : EditForm
{
    private readonly Func<Task> _handleSubmitCached;
    private readonly EventHandler<FieldChangedEventArgs> _editContextFieldChangedCached;

    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool IsHorizontal { get; set; }
    [Parameter] public string Id { get; set; } = "";

    public bool IsValid { get; private set; } = true;

    public Form() // The same private field declared in the base class, we just need to pull its value here
    {
        _handleSubmitCached = (Func<Task>)typeof(EditForm)
            .GetField("_handleSubmitDelegate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(this)!;
        _editContextFieldChangedCached = (sender, args) => {
            if (sender is not EditContext editContext)
                return;
            IsValid = editContext.Validate();
            StateHasChanged();
        };
    }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (EditContext is not { } editContext)
            return;

        IsValid = editContext.Validate();
        editContext.OnFieldChanged += _editContextFieldChangedCached;
        StateHasChanged();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (!firstRender)
            return;

        if (EditContext is not { } editContext)
            return;

        IsValid = editContext.Validate();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var editContext = EditContext;
        Debug.Assert(editContext != null);

        // If _editContext changes, tear down and recreate all descendants.
        // This is so we can safely use the IsFixed optimization on CascadingValue,
        // optimizing for the common case where _editContext never changes.
#pragma warning disable MA0123
        builder.OpenRegion(editContext.GetHashCode());

        var i = 0;
        builder.OpenElement(i++, "form");
        builder.AddAttribute(i++, "class", $"form {(IsHorizontal ? "form-x" : "form-y")} {Class}");
        builder.AddMultipleAttributes(i++, AdditionalAttributes);
        builder.AddAttribute(i++, "onsubmit", _handleSubmitCached);
        builder.OpenComponent<CascadingValue<EditContext>>(i++);
        builder.AddAttribute(i++, "IsFixed", true);
        builder.AddAttribute(i++, "Value", editContext);
        builder.AddAttribute(i++, "ChildContent", ChildContent?.Invoke(editContext));
        builder.CloseComponent();
        builder.CloseElement();

        builder.CloseRegion();
#pragma warning restore MA0123
    }
}
