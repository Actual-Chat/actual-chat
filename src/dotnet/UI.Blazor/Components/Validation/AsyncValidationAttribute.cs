using System.ComponentModel.DataAnnotations;

namespace ActualChat.UI.Blazor.Components;

public abstract class AsyncValidationAttribute : Attribute
{
    public string? ErrorMessage { get; init; }
    public abstract Task<ValidationResult?> IsValidAsync(object? value, ValidationContext validationContext, CancellationToken cancellationToken);
}
