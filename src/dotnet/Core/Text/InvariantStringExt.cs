namespace ActualChat;

public static class InvariantStringExt
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string? ToInvariantString<T>(this T? value)
        where T : IConvertible
        => value?.ToString(CultureInfo.InvariantCulture);

    [return: NotNullIfNotNull(nameof(value))]
    public static string? ToInvariantString<T>(this T? value, string format)
        where T : IFormattable
        => value?.ToString(format, CultureInfo.InvariantCulture);
}
