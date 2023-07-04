namespace ActualChat.DependencyInjection;

public static class TypeMapDictionaryExt
{
    public static Dictionary<Type, Type> Add<TKey, TValue>(this Dictionary<Type, Type> source)
    {
        source.Add(typeof(TKey), typeof(TValue));
        return source;
    }

    public static Dictionary<Type, Type> AddGeneric(this Dictionary<Type, Type> source, Type key, Type value)
    {
        source.Add(key, value);
        return source;
    }
}
