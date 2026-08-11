namespace ActualChat.Validation;

/// <summary>
/// Translation keys reported by our own validation attributes, which emit a key instead of an
/// English sentence. Messages from BCL attributes are reverse-indexed by <c>MessageIndex</c>.
/// </summary>
public static class ValidationKeys
{
    public const string Prefix = "Validation_";

    public const string EmailInvalid = Prefix + "EmailInvalid";
    public const string PhoneInvalidCharacters = Prefix + "PhoneInvalidCharacters";
    public const string PhoneTooShort = Prefix + "PhoneTooShort";
    public const string PhoneTooLong = Prefix + "PhoneTooLong";
    public const string PhoneOrEmailRequired = Prefix + "PhoneOrEmailRequired";
    public const string AliasTooShort = Prefix + "AliasTooShort";
    public const string AliasInvalidCharacters = Prefix + "AliasInvalidCharacters";

    public static readonly string[] All = [
        EmailInvalid,
        PhoneInvalidCharacters,
        PhoneTooShort,
        PhoneTooLong,
        PhoneOrEmailRequired,
        AliasTooShort,
        AliasInvalidCharacters,
    ];
}
