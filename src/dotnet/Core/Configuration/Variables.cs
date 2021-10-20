namespace ActualChat.Configuration;

public static class Variables
{
    public static string Inject(string source, IEnumerable<KeyValuePair<string, string>> variables)
    {
        foreach (var (k, v) in variables)
            source = source.Replace($"{{{k}}}", v, StringComparison.Ordinal);
        return source;
    }

    public static string Inject(string source, params (string Key, string Value)[] variables)
    {
        foreach (var (k, v) in variables)
            source = source.Replace($"{{{k}}}", v, StringComparison.Ordinal);
        return source;
    }
}
