using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ActualChat.AspNetCore;

public class OptionPropertyValidationFilter : IPropertyValidationFilter
{
    public static readonly OptionPropertyValidationFilter Instance = new ();

    public bool ShouldValidateEntry(ValidationEntry entry, ValidationEntry parentEntry)
        => !OrdinalEquals(entry.Metadata.Name,  nameof(Option<object>.Value));
}
