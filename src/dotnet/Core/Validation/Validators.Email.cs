using System.Net.Mail;

namespace ActualChat.Validation;

public static partial class Validators
{
    public static class Email
    {
        private const int MaxLength = 320; // RFC 5321

        /// <summary>Returns a <see cref="ValidationKeys"/> entry, or null if valid. Empty input = valid (use [Required] separately).</summary>
        public static string? Validate(string? input)
        {
            if (input.IsNullOrEmpty() || input.Trim() is var trimmed && trimmed.IsNullOrEmpty())
                return null;

            if (trimmed.Length > MaxLength)
                return ValidationKeys.EmailInvalid;

            if (!MailAddress.TryCreate(trimmed, out var mailAddress))
                return ValidationKeys.EmailInvalid;

            // Ensure parsed address matches the original input (catches display name injection)
            if (mailAddress.Address != trimmed)
                return ValidationKeys.EmailInvalid;

            // MailAddress accepts a dot-less host ("a@b"); require a real domain with a TLD.
            if (!HasValidDomain(mailAddress.Host))
                return ValidationKeys.EmailInvalid;

            return null;
        }

        public static bool HasValidDomain(string? host)
        {
            if (host.IsNullOrEmpty())
                return false;

            var lastDot = host.LastIndexOf('.');
            return lastDot > 0 && host.Length - lastDot - 1 >= 2;
        }
    }
}
