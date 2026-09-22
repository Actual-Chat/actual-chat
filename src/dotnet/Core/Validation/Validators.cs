namespace ActualChat.Validation;

public static partial class Validators
{
    public static bool IsEmailLike(string? input)
        => (input ?? "").Contains('@', StringComparison.Ordinal);

    public static bool IsPhoneLike(string? input)
    {
        // A leading '+' or digit picks the phone branch; a leading letter falls through to email.
        var s = (input ?? "").TrimStart();
        return s.Length > 0 && (s[0] == '+' || char.IsDigit(s[0]));
    }
}
