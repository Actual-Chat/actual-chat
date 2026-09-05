using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.Components;

public class Form : EditForm, IDisposable
{
    private readonly Func<Task> _handleSubmitCached;
    private EditContext? _editContext;

    [Parameter] public string Class { get; set; } = "";
    [Parameter] public bool IsHorizontal { get; set; }
    [Parameter] public string Id { get; set; } = "";

    public bool IsValid { get; private set; } = true;

    public Form()
    {
        // The same private field is declared in the base class - we just pull its value here.
        _handleSubmitCached = HandleSubmitDelegate(this);
    }

    public void Dispose()
    {
        if (_editContext is { } ctx) {
            ctx.OnFieldChanged -= EditContextFieldChangedCached;
            ctx.OnValidationStateChanged -= OnEditContextValidationStateChanged;
        }
    }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        var editContext = EditContext;
        if (ReferenceEquals(editContext, _editContext))
            return;

        if (_editContext != null) {
            _editContext.OnFieldChanged -= EditContextFieldChangedCached;
            _editContext.OnValidationStateChanged -= OnEditContextValidationStateChanged;
        }

        _editContext = editContext;
        if (editContext == null)
            return;

        editContext.OnFieldChanged += EditContextFieldChangedCached;
        editContext.OnValidationStateChanged += OnEditContextValidationStateChanged;
        IsValid = editContext.Validate();
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

    private void OnEditContextValidationStateChanged(object? sender, ValidationStateChangedEventArgs e)
    {
        if (sender is not EditContext editContext)
            return;

        // handling async validation results
        var isValid = !editContext.GetValidationMessages().Any();
        if (IsValid == isValid)
            return;

        IsValid = isValid;
        StateHasChanged();
    }

    private void EditContextFieldChangedCached(object? sender, FieldChangedEventArgs e)
    {
        if (sender is not EditContext editContext)
            return;

        // NOTE: though it triggers async validation as well, but only synchronous validation results are returned here.
        // For async results we listen ValidationStateChanged event below
        var isValid = editContext.Validate();
        if (IsValid == isValid)
            return;

        IsValid = isValid;
        StateHasChanged();
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_handleSubmitDelegate")]
    private static extern ref Func<Task> HandleSubmitDelegate(EditForm @this);
}
