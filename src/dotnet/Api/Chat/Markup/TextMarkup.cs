using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Base class for text-based markup elements.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
public abstract class TextMarkup(string text) : Markup
{
    public static TextMarkup New(TextMarkupKind kind, string text)
    {
        if (text.IsNullOrEmpty())
            return kind switch {
                TextMarkupKind.Plain => PlainTextMarkup.Empty,
                TextMarkupKind.Preformatted => PreformattedTextMarkup.Empty,
                TextMarkupKind.Unparsed => UnparsedTextMarkup.Empty,
                TextMarkupKind.NewLine => NewLineMarkup.Instance,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
            };

        return kind switch {
            TextMarkupKind.Plain => new PlainTextMarkup(text),
            TextMarkupKind.Preformatted => new PreformattedTextMarkup(text),
            TextMarkupKind.Unparsed => new UnparsedTextMarkup(text),
            TextMarkupKind.NewLine => throw new ArgumentOutOfRangeException(nameof(text)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    public static Markup New(TextMarkupKind kind, string text, bool parseNewLines)
    {
        if (kind is not (TextMarkupKind.Plain or TextMarkupKind.Preformatted or TextMarkupKind.Unparsed))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        if (text.IsNullOrEmpty())
            return EmptyText;
        if (!parseNewLines)
            return New(kind, text);

        var parts = new RefArrayPoolBuffer<Markup>(MarkupArrayPool, 64, mustClear: true);
        try {
            foreach (var (line, endsWithLineFeed) in text.ParseLines()) {
                parts.Add(New(kind, line));
                if (endsWithLineFeed)
                    parts.Add(NewLineMarkup.Instance);
            }
            return new MarkupSeq(parts.ToArray());
        }
        finally {
            parts.Release();
        }
    }

    [DataMember, Key(0)]
    public string Text { get; } = text;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public virtual TextMarkupKind Kind => TextMarkupKind.Unknown;

    public override string ToString()
        => $"{GetType()}({Format()}, Kind={Kind})";

    public override string Format()
        => Text;

    public abstract TextMarkup WithText(string text);
}
