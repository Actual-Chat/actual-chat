using ActualLab.Generators;

namespace ActualChat.Testing;

public static class UniqueNames
{
    private const int RandomPartLength = 5;
    private static readonly RandomStringGenerator Rsg = new (RandomPartLength, Alphabet.AlphaLower);
    private static readonly RandomStringGenerator Rng = new (10, Alphabet.Numeric);

    public static string Random(int length = RandomPartLength)
        => Rsg.Next(length);

    public static string User(int i)
        => Name($"User_{i}");

    public static string Chat(int i)
        => Name($"Chat {i}");

    public static string Place(int i)
        => Name($"Place {i}");

    public static string Name(string prefix, string delimiter = "_", int randomSuffixLength = RandomPartLength)
    {
        if (!prefix.IsNullOrEmpty())
            prefix = prefix.EnsureSuffix(delimiter);
        return prefix + Rsg.Next(randomSuffixLength);
    }

    public static Phone Phone(string countryCode = "1")
        => ActualChat.Phone.New(countryCode, Rng.Next(11 - countryCode.Length));

    public static string GoogleId()
        => Rng.Next(5);

    public static string AppleId(string prefix = "apple")
        => Name(prefix, "-");

    public static string Email(string prefix, string domain = Constants.Team.EmailDomain)
        => $"{prefix}.{Rsg.Next()}@{domain}";
}
