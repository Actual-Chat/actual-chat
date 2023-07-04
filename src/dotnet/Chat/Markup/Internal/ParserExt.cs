using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;
using Unit = System.Reactive.Unit;

namespace ActualChat.Chat;

internal static class ParserExt
{
    // Debug

    public static Action<string>? DebugOutput { get; set; }

    public static Parser<char, T> Debug<T>(this Parser<char, T> parser, Func<T, string> formatter) where T: class
        => Constants.DebugMode.MarkupParser
            ? parser.Select(x => {
                DebugOutput?.Invoke(formatter(x));
                return x;
            })
            : parser;
    public static Parser<char, T> Debug<T>(this Parser<char, T> parser, string title) where T: class
        => parser.Debug(x => $"{title}: {x}");

    // Extensions

    public static Parser<char, Markup> ToTextMarkup(this Parser<char, string> parser, TextMarkupKind textMarkupKind, bool parseNewLines)
        => parser.Select(s => TextMarkup.New(textMarkupKind, s, parseNewLines));

    public static Parser<char, char> OrEnd(this Parser<char, char> parser)
        => End.ThenReturn(default(char)).Or(parser);

    public static Parser<char, Markup> JoinMarkup(this Parser<char, Markup> head, Parser<char, Markup> tail) =>
        from h in head
        from t in tail
        select h + t;

    public static Parser<char, Markup> ManyMarkup(this Parser<char, Markup> markup) =>
        from m in markup.Many()
        select Markup.Join(m);

    public static Parser<char, Markup> AtLeastOnceMarkup(this Parser<char, Markup> markup) =>
        from m in markup.AtLeastOnce()
        select Markup.Join(m);

    public static Parser<char, Markup> AtLeastOnceInlineMarkup(this Parser<char, Markup> markup) =>
        markup
            .JoinMarkup(Try(MarkupParser.WhitespaceText.JoinMarkup(markup)).ManyMarkup())
            .JoinMarkup(Try(MarkupParser.WhitespaceText).Or(Nothing.ThenReturn(Markup.Empty)))
            .Debug("1+ inline");

    // Helper properties & methods

    public static readonly Parser<char, Unit> Nothing = new NothingParser();

    public static Parser<char, T> SafeTryOneOf<T>(params Parser<char, T>[] parsers) where T: class
        => OneOf(parsers.Select(Try));

    public static Parser<char, T> TryOneOf<T>(params Parser<char, T>[] parsers) where T: class
    {
        var newParsers = new Parser<char, T>[parsers.Length];
        var lastIndex = parsers.Length - 1;
        for (var i = 0; i < parsers.Length; i++) {
            var parser = parsers[i];
            newParsers[i] = i == lastIndex ? parser : Try(parser);
        }
        return OneOf(newParsers);
    }

    // Nested types

    private class NothingParser : Parser<char, Unit>
    {
        public override bool TryParse(ref ParseState<char> state, ref PooledList<Expected<char>> expecteds, out Unit result)
            => true;
    }
}
