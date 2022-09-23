using System.Text;

namespace ActualChat.Chat;

public abstract record TextMarkup(string Text) : Markup
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
            return Empty;
        if (!parseNewLines)
            return New(kind, text);

        var parts = MemoryBuffer<Markup>.Lease(true);
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

    public virtual TextMarkupKind Kind => TextMarkupKind.Unknown;

    public override string Format()
        => Text;

    protected override bool PrintMembers(StringBuilder builder)
    {
        builder.Append(nameof(Text)).Append(" = \"");
        builder.Append(Text.OrdinalReplace("\"", "\\\""));
        builder.Append('"');
        return true; // Indicates there is no comma / tail "}" must be prefixed with space
    }
}
