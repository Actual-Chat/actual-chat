namespace ActualChat.Collections;

public static class ApiMapExt
{
    public static ApiMap<TKey, TValue> With<TKey, TValue>(this ApiMap<TKey, TValue> map, TKey key, TValue value)
        where TKey : notnull
    {
        var newMap = map.Clone();
        newMap[key] = value;
        return newMap;
    }

    public static ApiMap<TKey, TValue> With<TKey, TValue>(this ApiMap<TKey, TValue> map, KeyValuePair<TKey, TValue> pair)
        where TKey : notnull
    {
        var newMap = map.Clone();
        newMap[pair.Key] = pair.Value;
        return newMap;
    }

    public static ApiMap<TKey, TValue> Without<TKey, TValue>(this ApiMap<TKey, TValue> map, TKey key)
        where TKey : notnull
    {
        var newMap = map.Clone();
        newMap.Remove(key);
        return newMap;
    }

    public static ApiMap<TKey, TValue> Without<TKey, TValue>(this ApiMap<TKey, TValue> map, params TKey[] keys)
        where TKey : notnull
    {
        var newMap = map.Clone();
        foreach (var key in keys)
            newMap.Remove(key);
        return newMap;
    }
}
