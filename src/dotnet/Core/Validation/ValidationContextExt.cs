using System.ComponentModel.DataAnnotations;

namespace ActualChat.Validation;

public static class ValidationContextExt
{
    public static ValidationResult Error(this ValidationContext context, string message)
        => new (message, [context.MemberName ?? ""]);
}
