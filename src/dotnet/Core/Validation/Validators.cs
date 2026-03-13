namespace ActualChat.Validation;

public static partial class Validators
{
    public static bool IsEmailLike(string? input)
        => (input ?? "").Contains('@', StringComparison.Ordinal);

    public static bool IsPhoneLike(string? input)
    {
        input ??= "";
        return input.StartsWith('+') || input.Any(char.IsDigit);
    }
}
