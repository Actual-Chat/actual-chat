namespace ActualChat.UI.Blazor.Components;

// TODO(FC): delete this if nothing ever needs an AsyncValidationAttribute - as of 2026-08 no
// subclass exists, and the forms using this component carry only synchronous attributes.
// Otherwise replace it with the built-in <DataAnnotationsValidator/>, but not before
// Microsoft.Extensions.Validation leaves preview: it is the only thing that makes the built-in
// validator run async attributes, its [ValidatableType]/[SkipValidation] are [Experimental]
// (ASP0029, an error by default), and [ValidatableType] on a model declared in a .razor file is
// ignored with no diagnostic at all - such models must move to a .razor.cs code-behind.
// Not experimental, and hence not blocking: the BCL AsyncValidationAttribute this stack already
// runs on, and EditContext.ValidateAsync / IsValidationPending / IsValidationFaulted.
public sealed class AsyncDataAnnotationsValidator : ComponentBase, IAsyncDisposable
{
    private EditContextAsyncValidator? _subscriptions;
    private EditContext? _originalEditContext;

    [CascadingParameter] private EditContext? CurrentEditContext { get; set; }

    [Inject] private UIHub Hub { get; set; } = null!;

    protected override void OnInitialized()
    {
        if (CurrentEditContext == null)
            throw new InvalidOperationException($"{nameof(AsyncDataAnnotationsValidator)} requires a cascading "
                + $"parameter of type {nameof(EditContext)}. For example, you can use "
                + $"{nameof(AsyncDataAnnotationsValidator)} inside an EditForm.");

        _subscriptions = new EditContextAsyncValidator(CurrentEditContext, Hub).Start();
        _originalEditContext = CurrentEditContext;
    }

    public async ValueTask DisposeAsync()
    {
        await _subscriptions.DisposeSilentlyAsync();
        _subscriptions = null;
    }

    public async Task<bool> Validate(CancellationToken cancellationToken = default)
    {
        if (_subscriptions is null)
            return true;

        return await _subscriptions.Validate(cancellationToken);
    }

    protected override void OnParametersSet()
    {
        if (CurrentEditContext != _originalEditContext) {
            // While we could support this, there's no known use case presently. Since InputBase doesn't support it,
            // it's more understandable to have the same restriction.
            throw new InvalidOperationException($"{GetType()} does not support changing the "
                + $"{nameof(EditContext)} dynamically.");
        }
    }
}
