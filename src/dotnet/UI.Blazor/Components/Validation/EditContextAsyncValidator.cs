using System.ComponentModel.DataAnnotations;
using ActualLab.Locking;

namespace ActualChat.UI.Blazor.Components;

public sealed class EditContextAsyncValidator : WorkerBase
{
    private readonly AsyncLock _lock = new ();
    private readonly Channel<FieldIdentifier?> _validationRequests = Channel.CreateBounded<FieldIdentifier?>(
        new BoundedChannelOptions(100) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private readonly EditContext _editContext;
    private readonly ValidationMessageStore _messages;

    private UIHub Hub { get; }
    private ILogger Log { get; }
    private ValidationModelStore ValidationModelStore { get; }
    private AsyncValidator AsyncValidator { get; }

    public EditContextAsyncValidator(EditContext editContext, UIHub hub)
    {
        _editContext = editContext ?? throw new ArgumentNullException(nameof(editContext));
        _messages = new ValidationMessageStore(_editContext);
        Hub = hub;
        ValidationModelStore = hub.GetRequiredService<ValidationModelStore>();
        AsyncValidator = hub.GetRequiredService<AsyncValidator>();
        Log = hub.LogFor(GetType());

        _editContext.OnFieldChanged += OnFieldChanged;
        _editContext.OnValidationRequested += OnValidationRequested;
    }

    protected override Task DisposeAsyncCore()
    {
        _validationRequests.Writer.TryComplete();
        _messages.Clear();
        _editContext.OnFieldChanged -= OnFieldChanged;
        _editContext.OnValidationRequested -= OnValidationRequested;
        _editContext.NotifyValidationStateChanged();
        return base.DisposeAsyncCore();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be untrimmed.")]
    public Task<bool> Validate(CancellationToken cancellationToken = default)
        => ValidateAll(cancellationToken);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be untrimmed.")]
    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await foreach (var fieldIdentifier in _validationRequests.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            try {
                using var cts = cancellationToken.CreateDelayedTokenSource(TimeSpan.FromSeconds(10));
                await Handle(fieldIdentifier, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException e) when (e.IsCancellationOf(cancellationToken)) { }
            catch (Exception e) {
                Log.LogError(e, "Failed to validate {ModelType}", _editContext.Model.GetType());
            }
        return;

        Task Handle(FieldIdentifier? fieldIdentifier, CancellationToken cancellationToken1)
            => fieldIdentifier is null
                ? ValidateAll(cancellationToken1)
                : ValidateProperty(fieldIdentifier.Value, cancellationToken1);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be untrimmed.")]
    private async Task ValidateProperty(FieldIdentifier fieldIdentifier, CancellationToken cancellationToken)
    {
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        var validationContext = new ValidationContext(_editContext.Model, Hub, null) {
            MemberName = fieldIdentifier.FieldName,
        };
        var ctx = ValidationModelStore.Get(validationContext);
        if (ctx == null)
            return;

        var results = new List<ValidationResult>();
        Validator.TryValidateProperty(ctx.Value, validationContext, results);
        _messages.Clear(fieldIdentifier);
        await AddValidationResults(results).ConfigureAwait(false);
        var asyncValidationResults = await AsyncValidator
            .ValidateProperty(validationContext.ObjectInstance, validationContext, cancellationToken)
            .ConfigureAwait(false);
        await AddValidationResults(asyncValidationResults).ConfigureAwait(false);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be untrimmed.")]
    private async Task<bool> ValidateAll(CancellationToken cancellationToken)
    {
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        var validationContext = new ValidationContext(_editContext.Model, Hub, null);
        _messages.Clear();
        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(_editContext.Model, validationContext, validationResults, true);
        await AddValidationResults(validationResults).ConfigureAwait(false);
        var asyncValidationResults = await AsyncValidator.Validate(validationContext, cancellationToken).ConfigureAwait(false);
        await AddValidationResults(asyncValidationResults).ConfigureAwait(false);
        return validationResults.Count == 0 && asyncValidationResults.Count == 0;
    }

    private void OnFieldChanged(object? sender, FieldChangedEventArgs e)
        => _validationRequests.Writer.TryWrite(e.FieldIdentifier);

    private void OnValidationRequested(object? sender, ValidationRequestedEventArgs e)
        => _validationRequests.Writer.TryWrite(null);

    private Task AddValidationResults(IReadOnlyCollection<ValidationResult> validationResults)
        => Hub.Dispatcher.InvokeAsync(() => {
            foreach (var validationResult in validationResults) {
                var hasMemberNames = false;
                foreach (var memberName in validationResult.MemberNames) {
                    hasMemberNames = true;
                    _messages.Add(_editContext.Field(memberName), validationResult.ErrorMessage!);
                }

                if (!hasMemberNames)
                    _messages.Add(new FieldIdentifier(_editContext.Model, fieldName: string.Empty), validationResult.ErrorMessage!);
            }
            _editContext.NotifyValidationStateChanged();
        });
}
