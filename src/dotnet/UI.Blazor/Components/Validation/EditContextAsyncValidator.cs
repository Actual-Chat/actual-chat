using System.ComponentModel.DataAnnotations;
using ActualLab.Locking;

namespace ActualChat.UI.Blazor.Components;

// TODO(FC): remove or replace along with AsyncDataAnnotationsValidator - see the note there.
// The standard equivalents are EditContext.ValidateAsync / IsValidationPending / IsValidationFaulted,
// which also supersede Form.IsValid and ProviderSelectStep's hand-rolled ValidationState.
public sealed class EditContextAsyncValidator : WorkerBase
{
    private static readonly ConcurrentDictionary<(Type ModelType, string FieldName), PropertyInfo?> PropertyCache
        = new ();

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

    public Task<bool> Validate(CancellationToken cancellationToken = default)
        => ValidateAll(cancellationToken);

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var requests = _validationRequests.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var fieldIdentifier in requests)
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

    private async Task<bool> ValidateAll(CancellationToken cancellationToken)
    {
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        var model = _editContext.Model;
        var validationContext = new ValidationContext(model, Services, null);
        var validationResults = new List<ValidationResult>();
        var isValid = await Validator
            .TryValidateObjectAsync(model, validationContext, validationResults, true, cancellationToken)
            .ConfigureAwait(false);
        await ClearAndAddValidationResults(model, null, validationResults).ConfigureAwait(false);
        return isValid;
    }

    private async Task ValidateProperty(FieldIdentifier fieldIdentifier, CancellationToken cancellationToken)
    {
        using var _ = await _lock.Lock(cancellationToken).ConfigureAwait(false);
        var property = GetProperty(fieldIdentifier);
        if (property is null)
            return;

        var validationContext = new ValidationContext(fieldIdentifier.Model, Services, null) {
            MemberName = property.Name,
        };
        var validationResults = new List<ValidationResult>();
        await Validator
            .TryValidatePropertyAsync(
                property.GetValue(fieldIdentifier.Model), validationContext, validationResults, cancellationToken)
            .ConfigureAwait(false);
        await ClearAndAddValidationResults(fieldIdentifier.Model, fieldIdentifier, validationResults)
            .ConfigureAwait(false);
    }

    private void OnFieldChanged(object? sender, FieldChangedEventArgs e)
        => _validationRequests.Writer.TryWrite(e.FieldIdentifier);

    private void OnValidationRequested(object? sender, ValidationRequestedEventArgs e)
        => _validationRequests.Writer.TryWrite(null);

    private Task ClearAndAddValidationResults(
        object model,
        FieldIdentifier? fieldIdentifier,
        IReadOnlyCollection<ValidationResult> validationResults)
        => Hub.Dispatcher.InvokeAsync(() => {
            if (fieldIdentifier is { } fi)
                _messages.Clear(fi);
            else
                _messages.Clear();
            AppendValidationResults(model, validationResults);
        });

    private void AppendValidationResults(object model, IReadOnlyCollection<ValidationResult> results)
    {
        foreach (var validationResult in results) {
            var hasMemberNames = false;
            foreach (var memberName in validationResult.MemberNames) {
                hasMemberNames = true;
                _messages.Add(new FieldIdentifier(model, memberName), validationResult.ErrorMessage!);
            }

            if (!hasMemberNames)
                _messages.Add(new FieldIdentifier(model, fieldName: string.Empty), validationResult.ErrorMessage!);
        }
        _editContext.NotifyValidationStateChanged();
    }

    private static PropertyInfo? GetProperty(FieldIdentifier fieldIdentifier)
        // Validator.TryValidatePropertyAsync throws for anything that isn't a public property,
        // so unknown fields (e.g. a FieldIdentifier built from a local) must be filtered out here.
        => PropertyCache.GetOrAdd(
            (fieldIdentifier.Model.GetType(), fieldIdentifier.FieldName),
            static key => GetProperty(key.ModelType, key.FieldName));

    private static PropertyInfo? GetProperty(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type modelType,
        string fieldName)
    {
        var property = modelType.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);
        return property?.CanRead == true ? property : null;
    }
}
