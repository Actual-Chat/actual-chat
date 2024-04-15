using System.ComponentModel.DataAnnotations;

namespace ActualChat.UI.Blazor.Components;

public sealed class AsyncValidator(ValidationModelStore modelStore)
{
    public async Task<IReadOnlyCollection<ValidationResult>> Validate(ValidationContext validationContext, CancellationToken cancellationToken)
    {
        var asyncValidationResults = await modelStore.List(validationContext)
            .Select(x => ValidateProperty(x, cancellationToken))
            .Collect();
        return asyncValidationResults.SelectMany(x => x).SkipNullItems().ToList();
    }

    public async Task<IReadOnlyCollection<ValidationResult>> ValidateProperty(object? value, ValidationContext validationContext, CancellationToken cancellationToken)
    {
        var ctx = modelStore.Get(validationContext.MemberName!, validationContext);
        if (ctx is null)
            return [];

        var validationResults = await ValidateProperty(ctx, cancellationToken).ConfigureAwait(false);
        return validationResults.SkipNullItems().ToList();
    }

    private static Task<ValidationResult?[]> ValidateProperty(ValidationModelStore.PropertyValidationContext ctx, CancellationToken cancellationToken = default)
        => ctx.Property.AsyncAttributes.Select(attr => attr.IsValidAsync(ctx.Value, ctx.ValidationContext, cancellationToken)).Collect();
}
