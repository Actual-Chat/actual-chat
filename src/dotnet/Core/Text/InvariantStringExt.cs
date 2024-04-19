using System.Diagnostics.CodeAnalysis;

namespace ActualChat;

public static class InvariantStringExt
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string? ToInvariantString(this IConvertible? value)
        => value?.ToString(CultureInfo.InvariantCulture);

    [return: NotNullIfNotNull(nameof(value))]
    public static string? ToInvariantString(this IFormattable? value, string format)
        => value?.ToString(format, CultureInfo.InvariantCulture);
}
