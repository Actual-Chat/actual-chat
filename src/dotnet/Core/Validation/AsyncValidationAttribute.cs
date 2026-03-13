using System.ComponentModel.DataAnnotations;

namespace ActualChat.Validation;

public abstract class AsyncValidationAttribute : ValidationAttribute
{
    // Sync part — called by Validator.TryValidateProperty / TryValidateObject
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        => ValidationResult.Success;

    // Async part — called by AsyncValidator only when sync part passed
    public virtual Task<ValidationResult?> IsValidAsync(
        object? value, ValidationContext validationContext, CancellationToken cancellationToken)
        => Task.FromResult(ValidationResult.Success);
}
