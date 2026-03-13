using System.ComponentModel.DataAnnotations;
using ActualLab.Locking;

namespace ActualChat.UI.Blazor.Components;

public sealed class EditContextAsyncValidator : WorkerBase
{
    private readonly AsyncLock _lock = new ();
    private readonly Channel<FieldIdentifier?> _validationRequests = ChannelExt.Create<FieldIdentifier?>(
        new BoundedChannelOptions(100) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private readonly EditContext _editContext;
    private readonly ValidationMessageStore _messages;

    private UIHub Hub { get; }
    private IServiceProvider Services => Hub.Services;
    private ILogger Log { get; }

    public EditContextAsyncValidator(EditContext editContext, UIHub hub)
    {
        _editContext = editContext ?? throw new ArgumentNullException(nameof(editContext));
        _messages = new ValidationMessageStore(_editContext);
        Hub = hub;
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

    // Private methods

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be untrimmed.")]
    private async Task<bool> ValidateAll(CancellationToken cancellationToken)
    {
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        var validationContext = new ValidationContext(_editContext.Model, Services, null);
        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(_editContext.Model, validationContext, validationResults, true);
        await ClearAndAddValidationResults(null, validationResults).ConfigureAwait(false);

        // Skip async validation for properties that already have sync errors
        var syncErrorMembers = validationResults.Count == 0
            ? null
            : validationResults.SelectMany(r => r.MemberNames).ToHashSet();
        var asyncValidationResults = await AsyncValidator
            .Validate(validationContext, syncErrorMembers, cancellationToken)
            .ConfigureAwait(false);
        await AddValidationResults(asyncValidationResults).ConfigureAwait(false);
        return validationResults.Count == 0 && asyncValidationResults.Count == 0;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be untrimmed.")]
    private async Task ValidateProperty(FieldIdentifier fieldIdentifier, CancellationToken cancellationToken)
    {
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        var validationContext = new ValidationContext(_editContext.Model, Services, null) {
            MemberName = fieldIdentifier.FieldName,
        };
        var ctx = AsyncValidationModel.CreatePropertyValidationContext(validationContext);
        if (ctx == null)
            return;

        var results = new List<ValidationResult>();
        Validator.TryValidateProperty(ctx.Value, validationContext, results);
        await ClearAndAddValidationResults(fieldIdentifier, results).ConfigureAwait(false);

        // Run async validation only if sync validation passed for this property
        if (results.Count == 0) {
            var asyncValidationResults = await AsyncValidator
                .ValidateProperty(validationContext, hasSyncErrors: false, cancellationToken)
                .ConfigureAwait(false);
            await AddValidationResults(asyncValidationResults).ConfigureAwait(false);
        }
    }

    private void OnFieldChanged(object? sender, FieldChangedEventArgs e)
        => _validationRequests.Writer.TryWrite(e.FieldIdentifier);

    private void OnValidationRequested(object? sender, ValidationRequestedEventArgs e)
        => _validationRequests.Writer.TryWrite(null);

    private Task ClearAndAddValidationResults(
        FieldIdentifier? fieldIdentifier, IReadOnlyCollection<ValidationResult> validationResults)
        => Hub.Dispatcher.InvokeAsync(() => {
            if (fieldIdentifier is { } fi)
                _messages.Clear(fi);
            else
                _messages.Clear();
            AppendValidationResults(validationResults);
        });

    private Task AddValidationResults(IReadOnlyCollection<ValidationResult> validationResults)
        => Hub.Dispatcher.InvokeAsync(() => AppendValidationResults(validationResults));

    private void AppendValidationResults(IReadOnlyCollection<ValidationResult> results)
    {
        foreach (var validationResult in results) {
            var hasMemberNames = false;
            foreach (var memberName in validationResult.MemberNames) {
                hasMemberNames = true;
                _messages.Add(_editContext.Field(memberName), validationResult.ErrorMessage!);
            }

            if (!hasMemberNames)
                _messages.Add(new FieldIdentifier(_editContext.Model, fieldName: string.Empty), validationResult.ErrorMessage!);
        }
        _editContext.NotifyValidationStateChanged();
    }
}
