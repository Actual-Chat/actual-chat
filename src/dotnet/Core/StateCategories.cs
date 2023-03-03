namespace ActualChat;

public static class StateCategories
{
    private static readonly ConcurrentDictionary<(Symbol, Symbol), string> CacheSS = new();
    private static readonly ConcurrentDictionary<(Type, Symbol), string> CacheTS = new();
    private static readonly ConcurrentDictionary<(Type, Symbol, Symbol), string> CacheTss = new();

    public static string Get(Symbol prefix, Symbol suffix)
        => CacheSS.GetOrAdd((prefix, suffix), static kv => $"{kv.Item1}.{kv.Item2.Value}");

    public static string Get(Type type, Symbol suffix)
        => CacheTS.GetOrAdd((type, suffix), static kv => $"{kv.Item1.GetName()}.{kv.Item2.Value}");

    public static string Get(Type type, Symbol suffix1, Symbol suffix2)
        => CacheTss.GetOrAdd((type, suffix1, suffix2), static kv => $"{kv.Item1.GetName()}.{kv.Item2.Value}.{kv.Item3.Value}");
}
