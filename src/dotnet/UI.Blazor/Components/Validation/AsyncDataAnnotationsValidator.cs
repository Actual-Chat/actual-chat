namespace ActualChat.UI.Blazor.Components;

public sealed class AsyncDataAnnotationsValidator : ComponentBase, IAsyncDisposable
{
    private EditContextAsyncValidator? _subscriptions;
    private EditContext? _originalEditContext;

    [CascadingParameter] private EditContext? CurrentEditContext { get; set; }

    [Inject] private UIHub UIHub { get; set; } = default!;

    protected override void OnInitialized()
    {
        if (CurrentEditContext == null)
            throw new InvalidOperationException($"{nameof(AsyncDataAnnotationsValidator)} requires a cascading "
                + $"parameter of type {nameof(EditContext)}. For example, you can use {nameof(AsyncDataAnnotationsValidator)} "
                + $"inside an EditForm.");

        _subscriptions = new EditContextAsyncValidator(CurrentEditContext, UIHub).Start();
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
