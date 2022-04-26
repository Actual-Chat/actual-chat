using System.Text.RegularExpressions;
using Cysharp.Text;
using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;
using Unit = System.Reactive.Unit;

namespace ActualChat.Chat.UnitTests.Markup2;

public static class MarkupParser2
{
    private static readonly bool IsDebugging = false;
    public static ITestOutputHelper? Out { get; set; }

    private static readonly Regex UrlRegex = new(
        @"^(ht|f)tp(s?)\:\/\/[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*(:(0-9)*)*(\/?)([a-zA-Z0-9\-\.\?\,\'\/\\\+&%\$#_]*)?$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Debug

    public static Parser<char, T> Debug<T>(this Parser<char, T> parser, Func<T, string> formatter)
        => IsDebugging
            ? parser.Select(x => {
                Out?.WriteLine(formatter(x));
                return x;
            })
            : parser;
    public static Parser<char, T> Debug<T>(this Parser<char, T> parser, string title)
        => parser.Debug(x => $"{title}: {x}");

    // Primitives

    private static Parser<char, Markup> ToPlainTextMarkup(this Parser<char, string> parser)
        => parser.Select(s => s.IsNullOrEmpty() ? Markup.Empty : new PlainTextMarkup(s));

    private static Parser<char, T> SafeTryOneOf<T>(params Parser<char, T>[] parsers)
        => OneOf(parsers.Select(Try));

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
        => End.ThenReturn(default(T)).Or(parser!);

    private static Parser<char, Markup> JoinMarkup(this Parser<char, Markup> head, Parser<char, Markup> tail) =>
        from h in head
        from t in tail
        select h + t;

    private static Parser<char, Markup> ManyMarkup(this Parser<char, Markup> markup) =>
        from m in markup.Many()
        select Markup.Join(m);

    private static Parser<char, Markup> AtLeastOnceMarkup(this Parser<char, Markup> markup) =>
        from m in markup.AtLeastOnce()
        select Markup.Join(m);

    private static Parser<char, Markup> AtLeastOnceInlineMarkup(this Parser<char, Markup> markup) =>
        markup
            .JoinMarkup(Try(DelimiterText.JoinMarkup(markup)).ManyMarkup())
            .JoinMarkup(Try(DelimiterText).Or(Nothing.ThenReturn(Markup.Empty)))
            .Debug("1+ inline");

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
    private static readonly Parser<char, char> WordChar =
        Token(c => char.IsLetterOrDigit(c) || c is '_').Labelled("letter, digit, or '_'");
    private static readonly Parser<char, char> NotWordChar =
        Token(c => !(char.IsLetterOrDigit(c) || c is '_')).Labelled("not letter, digit, or '_'");
    private static readonly Parser<char, char> SpecialChar =
        Token(c => c is '*' or '`' or '@').Labelled("'*', '`', or '@'");
    private static readonly Parser<char, char> NotSpecialOrWordChar =
        Token(c => !(char.IsLetterOrDigit(c) || c is '_' or '*' or '`' or '@'))
            .Labelled("not letter, digit, '_', '*', '`', or '@'");

    // Tokens

    private static readonly Parser<char, Unit> Nothing = new NoneParser();
    private static readonly Parser<char, TextStyle> BoldToken = String("**").WithResult(TextStyle.Bold);
    private static readonly Parser<char, TextStyle> ItalicToken = Char('*').WithResult(TextStyle.Italic);
    private static readonly Parser<char, char> PreToken = Char('`');
    private static readonly Parser<char, string> CodeBlockToken = String("```");
    private static readonly Parser<char, char> QuotedPreToken = PreToken.Then(PreToken);
    private static readonly Parser<char, char> NotPreChar = Token(c => c != '`');
    private static readonly Parser<char, char> AtToken = Char('@');

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

    // Word text & delimiter
    private static readonly Parser<char, Markup> WordText =
        WordChar.AtLeastOnceString().ToPlainTextMarkup();
    private static readonly Parser<char, Markup> DelimiterText =
        NotSpecialOrWordChar.AtLeastOnceString().ToPlainTextMarkup();
    private static readonly Parser<char, Markup> WhitespaceText =
        Whitespace.AtLeastOnceString().ToPlainTextMarkup();

    // Mention
    public static readonly Parser<char, Markup> Mention = AtToken.Then(TryOneOf(
        String("a:").Then(AuthorId).Select(s => (Markup)new Mention(s, MentionKind.AuthorId)),
        String("u:").Then(UserId).Select(s => (Markup)new Mention(s, MentionKind.UserId)),
        UserName.Select(s => (Markup)new Mention(s))
    ));

    // Url
    private static readonly Parser<char, Markup> Url =
        UrlChar.AtLeastOnceString()
            .Where(s => UrlRegex.IsMatch(s))
            .Select(s => (Markup)new UrlMarkup(s));

    // Preformatted text
    private static readonly Parser<char, Markup> PreformattedText =
        Lookahead(Not(CodeBlockToken.Before(NotPreChar.OrEnd())))
            .Then(NotPreChar.Or(Try(QuotedPreToken)).ManyString().Between(PreToken))
            .Select(s => (Markup)new PreformattedTextMarkup(s));

    // Mention | PreformattedText | Url | WordText
    private static readonly Parser<char, Markup> NonStylizedMarkup =
        SafeTryOneOf(Mention, PreformattedText, Url, WordText)
        .Debug("T");

    // Stylized text
    private static readonly Parser<char, Markup> BoldMarkup =
        Rec(() => TextBlock!).Between(Try(BoldToken))
            .Select(t => (Markup)new StylizedMarkup(t, TextStyle.Bold))
            .Debug("**");
    private static readonly Parser<char, Markup> ItalicMarkup =
        Rec(() => TextBlock!).Between(ItalicToken)
            .Select(t => (Markup)new StylizedMarkup(t, TextStyle.Italic))
            .Debug("*");

    // Text block
    private static readonly Parser<char, Markup> TextBlock =
        SafeTryOneOf(BoldMarkup, ItalicMarkup, NonStylizedMarkup)
            .AtLeastOnceInlineMarkup()
            .Debug("<Text>");

    // Code block
    private static readonly Parser<char, string> CodeBlockStart =
        CodeBlockToken
            .Then(StringIdChar.ManyString()) // Language
            .Before(SpaceOrTabChar.SkipUntil(EndOfLine));
    private static readonly Parser<char, char> CodeBlockEnd =
        SpaceOrTabChar.SkipMany().Then(CodeBlockToken).Then(Lookahead(Whitespace.OrEnd()));
    private static readonly Parser<char, string> CodeBlockLine =
        Lookahead(Not(CodeBlockEnd))
            .Then(NotEndOfLineChar.ManyString());
    private static readonly Parser<char, string> CodeBlockCode =
        Try(CodeBlockLine)
            .SeparatedAndTerminated(Try(EndOfLine))
            .Select(lines => {
                using var sb = ZString.CreateStringBuilder();
                foreach (var line in lines)
                    sb.AppendLine(line);
                return sb.ToString();
            });
    private static readonly Parser<char, Markup> CodeBlock = (
        from language in CodeBlockStart
        from code in Try(CodeBlockCode).Optional()
        from end in CodeBlockEnd
        select (Markup)new CodeBlockMarkup(code.GetValueOrDefault(""), language.TrimEnd())
        ).Debug("<Code>");

    // Whitespace block
    private static readonly Parser<char, Markup> WhitespaceBlock =
        Whitespace.AtLeastOnceString().ToPlainTextMarkup()
            .Debug("<Whitespace>");

    // Unparsed block
    private static readonly Parser<char, Markup> UnparsedBlock = (
        from whitespace in WhitespaceString
        from special in SpecialChar.AtLeastOnceString()
        select (Markup)new UnparsedMarkup(whitespace + special)
        ).Debug("<Unparsed>");

    // Blocks
    private static readonly Parser<char, Markup> BlocksMarkup =
        SafeTryOneOf(WhitespaceBlock, TextBlock, CodeBlock, UnparsedBlock).ManyMarkup();

    public static Markup Parse(string text)
    {
        var result = BlocksMarkup.Parse(text);
        return result.Success ? result.Value.Simplify() : new PlainTextMarkup(text);
    }

    // Nested types

    private class NoneParser : Parser<char, Unit>
    {
        public override bool TryParse(ref ParseState<char> state, ref PooledList<Expected<char>> expecteds, out Unit result)
            => true;
    }
}
