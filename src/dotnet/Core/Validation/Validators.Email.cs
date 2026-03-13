using System.Net.Mail;

namespace ActualChat.Validation;

public static partial class Validators
{
    public static class Email
    {
        private const int MaxLength = 320; // RFC 5321

        /// <summary>Returns error message or null if valid. Empty input = valid (use [Required] separately).</summary>
        public static string? Validate(string? input)
        {
            if (input.IsNullOrEmpty() || input.Trim() is var trimmed && trimmed.IsNullOrEmpty())
                return null;

            if (trimmed.Length > MaxLength)
                return "Email address is invalid.";

            if (!MailAddress.TryCreate(trimmed, out var mailAddress))
                return "Email address is invalid.";

            // Ensure parsed address matches the original input (catches display name injection)
            if (mailAddress.Address != trimmed)
                return "Email address is invalid.";

            return null;
        }
    }
}
