using Cysharp.Text;

namespace ActualChat.UI.Blazor.Services.Internal;

public sealed record HistoryItemIdFormatter(string Prefix)
{
    public HistoryItemIdFormatter()
        : this(Alphabet.AlphaNumericLower.Generator8.Next() + "-")
    { }

    public string Format(long id)
        => ZString.Concat(Prefix, id);

    public long? Parse(string? value)
    {
        if (value?.OrdinalStartsWith(Prefix) != true)
            return null;

        var suffix = value.AsSpan(Prefix.Length);
        return NumberExt.TryParsePositiveLong(suffix, out var result) ? result : null;
    }
}
