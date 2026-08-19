namespace ActualChat.Validation;

public static partial class Validators
{
    public static class Phone
    {
        public const int MinDigits = 8;
        public const int MaxDigits = 15;

        /// <summary>Returns a <see cref="ValidationKeys"/> entry, or null if valid. Empty input = valid (use [Required] separately).</summary>
        public static string? Validate(string? input)
        {
            if (input.IsNullOrEmpty() || input.Trim() is var trimmed && trimmed.IsNullOrEmpty())
                return null;

            // Auto-prepend '+' if input looks like a phone number without it
            var normalized = trimmed.StartsWith('+') ? trimmed : "+" + trimmed;

            if (!normalized.Skip(1).All(IsValidCharacter))
                return ValidationKeys.PhoneInvalidCharacters;

            var digitCount = normalized.Count(char.IsDigit);
            if (digitCount < MinDigits)
                return ValidationKeys.PhoneTooShort;
            if (digitCount > MaxDigits)
                return ValidationKeys.PhoneTooLong;

            return null;
        }

        // Private methods

        private static bool IsValidCharacter(char c)
            => char.IsDigit(c) || c is '-' or '(' or ')' or ' ';
    }
}
