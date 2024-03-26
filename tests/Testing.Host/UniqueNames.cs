using ActualLab.Generators;

namespace ActualChat.Testing.Host;

public static class UniqueNames
{
    private const int RandomPartLength = 5;
    private static readonly RandomStringGenerator Rsg = new (RandomPartLength, Alphabet.AlphaLower);

    public static string Elastic(string prefix)
        => Name(prefix, "-");

    public static string User(int i)
        => Name($"User_{i}");

    public static string Chat(int i)
        => Name($"Chat {i}");

    public static string Prefix(int length = RandomPartLength)
        => Rsg.Next(length);

    public static string Name(string prefix, string delimiter = "_", int randomSuffixLength = RandomPartLength)
    {
        if (!prefix.IsNullOrEmpty())
            prefix = prefix.EnsureSuffix(delimiter);
        return prefix + Rsg.Next(randomSuffixLength);
    }
}
