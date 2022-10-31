namespace ActualChat;

public static class Alphabet
{
    public static readonly string Numeric = "0123456789";
    public static readonly string AlphaLower = "abcdefghijklmnopqrstuvwxyz";
    public static readonly string AlphaUpper = AlphaLower.ToUpperInvariant();
    public static readonly string Alpha = AlphaLower + AlphaUpper;
    public static readonly string AlphaNumeric = Numeric + Alpha;
    public static readonly string AlphaNumericLower = Numeric + AlphaLower;
    public static readonly string AlphaNumericUpper = Numeric + AlphaUpper;
    public static readonly string AlphaNumeric64 = AlphaNumeric + "-_";
    public static readonly string Base64 = AlphaNumeric + "+/";
    public static readonly string Base32 = AlphaNumericLower[..32];
    public static readonly string Base16 = AlphaNumericLower[..16];
}
