using System.Text.RegularExpressions;
using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;
using Unit = System.Reactive.Unit;

namespace ActualChat.Chat.UnitTests.Markup2;

public static class MarkupParser2
{
    private static readonly Regex UrlRegex = new(
        @"^(ht|f)tp(s?)\:\/\/[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*(:(0-9)*)*(\/?)([a-zA-Z0-9\-\.\?\,\'\/\\\+&%\$#_]*)?$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Debug

    public static Parser<char, T> Debug<T>(this Parser<char, T> parser, Func<T, string> formatter)
        => parser.Select(x => {
            System.Diagnostics.Debug.WriteLine(formatter(x));
            return x;
        });

    // Primitives

    private static Parser<char, T> TryOneOf<T>(params Parser<char, T>[] parsers)
    {
        var newParsers = new Parser<char, T>[parsers.Length];
        var lastIndex = parsers.Length - 1;
        for (var i = 0; i < parsers.Length; i++) {
            var parser = parsers[i];
            newParsers[i] = i == lastIndex ? parser : Try(parser);
        }
        return OneOf(newParsers);
    }

    private static Parser<char, T?> OrEnd<T>(this Parser<char, T> parser)
        => End.WithResult(default(T)).Or(parser!);

    // Character classes

    private static readonly Parser<char, char> UrlChar =
        Token(c => char.IsLetterOrDigit(c) || ":/?&#+%_.\\-~".Contains(c, StringComparison.Ordinal)).Labelled("URL character");
    private static readonly Parser<char, char> SpaceOrTabChar =
        Token(c => c is ' ' or '\t').Labelled("space or tab");
    private static readonly Parser<char, char> EndOfLineChar =
        Token(c => c is ('\r' or '\n' or '\u2028')).Labelled("line separator");
    private static readonly Parser<char, char> NotEndOfLineChar =
        Token(c => c is not ('\r' or '\n' or '\u2028')).Labelled("not line separator");
    private static readonly Parser<char, char> StringIdChar =
        Token(c => char.IsLetterOrDigit(c) || c is '_' or '-').Labelled("letter, digit, '_', or '-'");

    // Tokens

    private static readonly Parser<char, TextStyle> BoldToken = String("**").WithResult(TextStyle.Bold);
    private static readonly Parser<char, TextStyle> ItalicToken = Char('*').WithResult(TextStyle.Italic);
    private static readonly Parser<char, char> PreToken = Char('`');
    private static readonly Parser<char, char> QuotedPreToken = PreToken.Then(PreToken);
    private static readonly Parser<char, char> NotPreChar = Token(c => c != '`');
    private static readonly Parser<char, char> AtToken = Char('@');
    private static readonly Parser<char, char> TokenChar = Token(c => c is ('*' or '`' or '@'));
    private static readonly Parser<char, char> NotTokenChar = Token(c => c is not ('*' or '`' or '@'));

    public static readonly Parser<char, string> UserId = StringIdChar.AtLeastOnceString();
    public static readonly Parser<char, string> ChatId = StringIdChar.AtLeastOnceString();
    public static readonly Parser<char, string> AuthorId =
        from chatId in ChatId
        from separator in Char(':')
        from authorId in Digit.AtLeastOnceString()
        select chatId + separator + authorId;

    private static readonly Parser<char, char> UserNameChar = StringIdChar;
    public static readonly Parser<char, string> UserName =
        from head in Letter
        from tail in UserNameChar.AtLeastOnceString().Where(s => s.Length >= 3)
        select head + tail;

    // Markup parsers

    // Mention
    public static readonly Parser<char, Mention> Mention = AtToken.Then(TryOneOf(
        String("a:").Then(AuthorId).Select(s => new Mention(s, MentionKind.AuthorId)),
        String("u:").Then(UserId).Select(s => new Mention(s, MentionKind.UserId)),
        UserName.Select(s => new Mention(s))
    ));

    // Code block
    private static readonly Parser<char, string> CodeBlockStartLine =
        from startPos in CurrentOffset.Where(o => o == 0).Or(EndOfLine.Select(_ => 0))
        from startSign in PreToken.Repeat(3)
        from language in StringIdChar.ManyString()
        from spaces in SpaceOrTabChar.Many()
        from eol in EndOfLine
        select language;
    private static readonly Parser<char, Unit> CodeBlockEndLine =
        from startSign in PreToken.Repeat(3)
        from spaces in SpaceOrTabChar.Many()
        from eol in EndOfLine.OrEnd()
        select default(Unit);
    private static readonly Parser<char, string> CodeBlockLine =
        from startSign in Not(Try(CodeBlockEndLine))
        from line in NotEndOfLineChar.ManyString()
        from eol in EndOfLine.OrEnd()
        select line;
    private static readonly Parser<char, string> CodeBlock =
        from startLine in CodeBlockStartLine
        from lines in CodeBlockLine.Many().Select(l => l.ToDelimitedString("\\r\\n"))
        from endLine in CodeBlockEndLine.OrEnd()
        select lines;

    // Url
    private static readonly Parser<char, UrlMarkup> Url =
        UrlChar.AtLeastOnceString()
            .Where(s => UrlRegex.IsMatch(s))
            .Select(s => new UrlMarkup(s));

    // Preformatted text
    private static readonly Parser<char, PreformattedTextMarkup> PreformattedText =
        NotPreChar.Or(Try(QuotedPreToken)).ManyString()
            .Between(PreToken)
            .Select(s => new PreformattedTextMarkup(s));

    // Plain text
    private static readonly Parser<char, PlainTextMarkup> PlainText =
        NotTokenChar.AtLeastOnceString().Select(s => new PlainTextMarkup(s));

    // Unparsed tokens
    private static readonly Parser<char, UnparsedMarkup> UnparsedTokens =
        TokenChar.AtLeastOnceString().Select(s => new UnparsedMarkup(s));

    // Text - basic block
    private static readonly Parser<char, TextMarkup> TextBase = TryOneOf(
        Mention.Cast<TextMarkup>(),
        PreformattedText.Cast<TextMarkup>(),
        Url.Cast<TextMarkup>(),
        Rec(() => StylizedText!).Cast<TextMarkup>(),
        PlainText.Cast<TextMarkup>()
    );

    // Text
    private static readonly Parser<char, TextMarkup> Text =
        TextBase.AtLeastOnce().Select(seq => {
            var list = seq.ToList();
            return list.Count == 1 ? list[0] : new TextMarkupSeq(ImmutableArray.Create(list.ToArray()));
        });

    // Stylized text
    private static readonly Parser<char, StylizedTextMarkup> BoldText =
        Text.Between(BoldToken).Select(t => new StylizedTextMarkup(t, TextStyle.Bold));
    private static readonly Parser<char, StylizedTextMarkup> ItalicText =
        Text.Between(ItalicToken).Select(t => new StylizedTextMarkup(t, TextStyle.Italic));
    private static readonly Parser<char, StylizedTextMarkup> StylizedText =
        TryOneOf(BoldText, ItalicText);

    // Markup
    private static readonly Parser<char, Markup> MarkupBase = TryOneOf(
        CodeBlock.Cast<Markup>(),
        Text.Cast<Markup>(),
        UnparsedTokens.Cast<Markup>()
    );

    // Markup sequence
    public static readonly Parser<char, Markup> Markup =
        MarkupBase.AtLeastOnce().Select(seq => {
            var list = seq.ToList();
            return list.Count == 1 ? list[0] : new MarkupSeq(ImmutableArray.Create(list.ToArray()));
        });

    public static Markup Parse(string text)
    {
        var result = Markup.Parse(text);
        return result.Success ? result.Value : new PlainTextMarkup(text);
    }
}
