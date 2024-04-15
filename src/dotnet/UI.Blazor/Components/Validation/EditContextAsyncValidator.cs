using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using ActualLab.Locking;

namespace ActualChat.UI.Blazor.Components;

public sealed class EditContextAsyncValidator : WorkerBase
{
    private readonly SemaphoreSlim _lock = new (1);
    private readonly Channel<FieldIdentifier?> _validationRequests = Channel.CreateBounded<FieldIdentifier?>(
        new BoundedChannelOptions(100) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private readonly EditContext _editContext;
    private readonly ValidationMessageStore _messages;

    private ValidationModelStore ValidationModelStore { get; }
    private AsyncValidator AsyncValidator { get; }
    private UIHub UIHub { get; }
    private ILogger Log { get; }

    public EditContextAsyncValidator(EditContext editContext, UIHub uiHub)
    {
        _editContext = editContext ?? throw new ArgumentNullException(nameof(editContext));
        _messages = new ValidationMessageStore(_editContext);
        UIHub = uiHub;
        ValidationModelStore = uiHub.GetRequiredService<ValidationModelStore>();
        AsyncValidator = uiHub.GetRequiredService<AsyncValidator>();
        Log = uiHub.LogFor(GetType());

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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
    public Task<bool> Validate(CancellationToken cancellationToken = default)
        => ValidateAll(cancellationToken);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await foreach (var fieldIdentifier in _validationRequests.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            try {
                if (fieldIdentifier is null)
                    await ValidateAll(cancellationToken);
                else
                    await ValidateProperty(fieldIdentifier.Value, cancellationToken);
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to validate {ModelType}", _editContext.Model.GetType());
            }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
    private async Task ValidateProperty(FieldIdentifier fieldIdentifier, CancellationToken cancellationToken)
    {
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        var validationContext = new ValidationContext(_editContext.Model, UIHub, null) {
            MemberName = fieldIdentifier.FieldName,
        };
        var ctx = ValidationModelStore.Get(fieldIdentifier.FieldName, validationContext);
        if (ctx == null)
            return;

        var results = new List<ValidationResult>();
        Validator.TryValidateProperty(ctx.Value, validationContext, results);
        _messages.Clear(fieldIdentifier);
        await AddValidationResults(results);
        var asyncValidationResults = await AsyncValidator
            .ValidateProperty(validationContext.ObjectInstance, validationContext, cancellationToken)
            .ConfigureAwait(false);
        await AddValidationResults(asyncValidationResults).ConfigureAwait(false);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Model types are expected to be defined in assemblies that do not get trimmed.")]
    private async Task<bool> ValidateAll(CancellationToken cancellationToken)
    {
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        var validationContext = new ValidationContext(_editContext.Model, UIHub, null);
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

    private Task AddValidationResults(IEnumerable<ValidationResult> validationResults)
        => UIHub.Dispatcher.InvokeAsync(() => {
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
