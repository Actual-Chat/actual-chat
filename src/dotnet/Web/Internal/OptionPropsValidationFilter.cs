using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ActualChat.Web.Internal;

public class OptionPropsValidationFilter : IPropertyValidationFilter
{
    public static readonly OptionPropsValidationFilter Instance = new ();

    public bool ShouldValidateEntry(ValidationEntry entry, ValidationEntry parentEntry)
        => !OrdinalEquals(entry.Metadata.Name,  nameof(Option<object>.Value));
}
