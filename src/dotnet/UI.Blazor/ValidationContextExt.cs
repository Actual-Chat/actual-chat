using System.ComponentModel.DataAnnotations;

namespace ActualChat.UI.Blazor;

public static class ValidationContextExt
{
    public static ValidationResult Error(this ValidationContext context, string message)
        => new (message, new[] { context.MemberName ?? context.DisplayName });
}
