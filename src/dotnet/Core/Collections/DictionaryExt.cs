namespace ActualChat.Collections;

public static class DictionaryExt
{
    public static TValue? Get<TValue>(this IDictionary<string, object> dict, string key)
        => dict.Get<TValue?>(key, default);

    public static TValue Get<TValue>(this IDictionary<string, object> dict, string key, TValue @default)
        => dict.TryGetValue(key, out var value) ? (TValue)value : @default;

    public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key)
        where TKey : notnull
        where TValue : new()
    {
        if (!dict.TryGetValue(key, out var value)) {
            value = new ();
            dict.Add(key, value);
        }

        return value;
    }

    public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TValue> defaultValueProvider)
        where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var value)) {
            value = defaultValueProvider();
            dict.Add(key, value);
        }

        return value;
    }

    public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TKey, TValue> defaultValueProvider)
        where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var value)) {
            value = defaultValueProvider(key);
            dict.Add(key, value);
        }

        return value;
    }

    public static TValue GetOrAdd<TKey, TValue, TArg>(this IDictionary<TKey, TValue> dict, TKey key, Func<TKey, TArg, TValue> defaultValueProvider, TArg arg)
        where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var value)) {
            value = defaultValueProvider(key, arg);
            dict.Add(key, value);
        }

        return value;
    }

    public static void Set(this IDictionary<string, object> dict, string key, object value)
        => dict[key] = value;
}
