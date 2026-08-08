using System.ComponentModel.DataAnnotations;
using ActualChat.Localization;

namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// Runs DataAnnotations validation attribute by attribute, so each failure can be
/// re-rendered from <see cref="ValidationStrings"/> instead of the attribute's own message.
/// </summary>
public static class LocalizingValidator
{
    public static void ValidateObject(ValidationContext validationContext, List<ValidationResult> results)
    {
        var validatedType = AsyncValidationModel.Get(validationContext.ObjectType);
        foreach (var property in validatedType.Properties.Values) {
            var ctx = AsyncValidationModel.ValidatedType.CreatePropertyValidationContext(validationContext, property);
            ValidateProperty(ctx, results);
        }

        if (validationContext.ObjectInstance is IValidatableObject validatable)
            results.AddRange(validatable.Validate(validationContext).SkipNullItems());
    }

    public static void ValidateProperty(
        AsyncValidationModel.PropertyValidationContext ctx, List<ValidationResult> results)
    {
        // All attributes, async ones included: AsyncValidationAttribute has a sync half too,
        // and some (e.g. PhoneOrEmailAsync) do all their work there.
        var attributes = ctx.Property.Attributes;
        if (attributes.Count == 0)
            return;

        // Matches Validator's own ordering: a failed [Required] suppresses the rest for that property.
        var required = attributes.OfType<RequiredAttribute>().FirstOrDefault();
        var fieldName = ValidationMessageLocalizer.GetFieldName(
            ctx.ValidationContext.MemberName, ctx.Property.DisplayName);
        if (required is not null && !TryValidate(required, ctx, fieldName, results))
            return;

        foreach (var attribute in attributes) {
            if (!ReferenceEquals(attribute, required))
                TryValidate(attribute, ctx, fieldName, results);
        }
    }

    // Private methods

    private static bool TryValidate(
        ValidationAttribute attribute,
        AsyncValidationModel.PropertyValidationContext ctx,
        string fieldName,
        List<ValidationResult> results)
    {
        var result = attribute.GetValidationResult(ctx.Value, ctx.ValidationContext);
        if (result is null)
            return true;

        // An explicitly configured message wins - the catalog only supplies the default one.
        var message = (ValidationMessageLocalizer.HasExplicitMessage(attribute)
                ? null
                : ValidationMessageLocalizer.Localize(attribute, fieldName))
            ?? result.ErrorMessage
            ?? "";
        results.Add(new ValidationResult(message, [ctx.ValidationContext.MemberName ?? ""]));
        return false;
    }
}
