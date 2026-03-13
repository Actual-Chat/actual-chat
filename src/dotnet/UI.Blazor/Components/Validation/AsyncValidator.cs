using System.ComponentModel.DataAnnotations;

namespace ActualChat.UI.Blazor.Components;

public static class AsyncValidator
{
    public static async Task<IReadOnlyCollection<ValidationResult>> Validate(
        ValidationContext validationContext,
        IReadOnlySet<string>? syncErrorMembers,
        CancellationToken cancellationToken)
    {
        var validatedType = AsyncValidationModel.Get(validationContext.ObjectType);
        var contexts = validatedType.CreateAsyncPropertyValidationContexts(validationContext).AsEnumerable();
        if (syncErrorMembers is { Count: > 0 })
            contexts = contexts.Where(x => !syncErrorMembers.Contains(x.ValidationContext.MemberName ?? ""));

        var asyncValidationResults = await contexts
            .Select(x => ValidateProperty(x, cancellationToken))
            .Collect(ApiConstants.Concurrency.Unlimited, cancellationToken)
            .ConfigureAwait(false);
        return asyncValidationResults.SelectMany(x => x).SkipNullItems().ToList();
    }

    public static async Task<IReadOnlyCollection<ValidationResult>> ValidateProperty(
        ValidationContext validationContext,
        bool hasSyncErrors,
        CancellationToken cancellationToken)
    {
        if (hasSyncErrors)
            return [];

        var ctx = AsyncValidationModel.CreatePropertyValidationContext(validationContext);
        if (ctx is null)
            return [];

        var validationResults = await ValidateProperty(ctx, cancellationToken).ConfigureAwait(false);
        return validationResults.SkipNullItems().ToList();
    }

    // Private methods

    private static Task<ValidationResult?[]> ValidateProperty(
        AsyncValidationModel.PropertyValidationContext ctx, CancellationToken cancellationToken = default)
        => ctx.Property.AsyncAttributes
            .Select(attr => attr.IsValidAsync(ctx.Value, ctx.ValidationContext, cancellationToken))
            .Collect(ApiConstants.Concurrency.Unlimited, cancellationToken);
}
