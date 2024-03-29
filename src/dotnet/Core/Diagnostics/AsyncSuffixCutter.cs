namespace ActualChat.Diagnostics;

public static class AsyncSuffixCutter
{
    private static readonly ConcurrentDictionary<string, string> CutAsyncSuffixCache = new(StringComparer.Ordinal);

    public static string CutAsyncSuffix(this string value)
    {
        if (value.IsNullOrEmpty())
            return value;

        if (value.Length > 5 && value.OrdinalEndsWith("Async"))
            value = CutAsyncSuffixCache.GetOrAdd(value, n => n[..^5]);
        return value;
    }
}
