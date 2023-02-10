using Cysharp.Text;

namespace ActualChat.UI.Blazor.Services.Internal;

public sealed record HistoryPositionFormatter(string Prefix)
{
    public HistoryPositionFormatter()
        : this(Alphabet.AlphaNumericLower.Generator8.Next() + "-")
    { }

    public string Format(int position)
        => ZString.Concat(Prefix, position);

    public int? Parse(string? value)
    {
        if (value?.OrdinalStartsWith(Prefix) != true)
            return null;

        var suffix = value.AsSpan(Prefix.Length);
        if (!int.TryParse(suffix, CultureInfo.InvariantCulture, out var result))
            return null;
        if (result is < 0 or > HistoryUI.MaxPosition)
            return null;

        return result;
    }
}
