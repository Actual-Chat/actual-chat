namespace ActualChat.Collections;

public static class DictionaryExt
{
    public static TValue? Get<TValue>(this IDictionary<string, object> dict, string key)
        => dict.Get<TValue?>(key, default);

    public static TValue Get<TValue>(this IDictionary<string, object> dict, string key, TValue @default)
        => dict.TryGetValue(key, out var value) ? (TValue)value : @default;

    public static void Set(this IDictionary<string, object> dict, string key, object value)
        => dict[key] = value;
}
