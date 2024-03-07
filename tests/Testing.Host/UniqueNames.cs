using ActualLab.Generators;

namespace ActualChat.Testing.Host;

public static class UniqueNames
{
    private static readonly RandomStringGenerator Rsg = new (5, Alphabet.AlphaLower);

    public static string HostInstance(string prefix)
    {
        if (!prefix.IsNullOrEmpty())
            prefix = prefix.EnsureSuffix("_");
        return prefix + Rsg.Next();
    }

    public static string Elastic(string prefix)
        => prefix.EnsureSuffix("-") + Rsg.Next();

    public static string User(int i)
        => User($"User_{i}");

    public static string User(string prefix)
    {
        if (!prefix.IsNullOrEmpty())
            prefix = prefix.EnsureSuffix("_");
        return prefix + Rsg.Next();
    }
}
