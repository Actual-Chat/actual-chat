using System.Text.RegularExpressions;
using Cysharp.Text;
using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;
using Unit = System.Reactive.Unit;

namespace ActualChat.Chat.UnitTests.Markup2;

public static class MarkupParser2
{
    public static ITestOutputHelper? Out { get; set; }

    private static readonly Regex UrlRegex = new(
        @"^(ht|f)tp(s?)\:\/\/[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*(:(0-9)*)*(\/?)([a-zA-Z0-9\-\.\?\,\'\/\\\+&%\$#_]*)?$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Debug

    public static Parser<char, T> Debug<T>(this Parser<char, T> parser, Func<T, string> formatter)
        => parser.Select(x => {
            Out?.WriteLine(formatter(x));
            return x;
        });

    // Primitives

    private static Parser<char, Unit> Expected<T>(this Parser<char, T> parser)
        => Not(parser.Unexpected()).ThenReturn(default(Unit));
    private static Parser<char, Unit> Unexpected<T>(this Parser<char, T> parser)
        => Try(Not(Try(parser))).ThenReturn(default(Unit));

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
    private static readonly Parser<char, string> CodeBlockToken = String("```");
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
    private static readonly Parser<char, string> CodeBlockStart =
        CodeBlockToken
            .Then(StringIdChar.ManyString()) // Language
            .Before(SpaceOrTabChar.SkipUntil(EndOfLine));
    private static readonly Parser<char, Unit> CodeBlockEnd =
        CodeBlockToken
            .ThenReturn(default(Unit));
    private static readonly Parser<char, string> CodeBlockLine =
        CodeBlockToken.Unexpected()
            .Then(NotEndOfLineChar.ManyString());
    private static readonly Parser<char, string> CodeBlockCode =
        CodeBlockLine
            .SeparatedAndTerminated(EndOfLine)
            .Select(lines => {
                using var sb = ZString.CreateStringBuilder();
                foreach (var line in lines)
                    sb.AppendLine(line);
                return sb.ToString();
            });
    private static readonly Parser<char, CodeBlockMarkup> CodeBlock =
        from language in CodeBlockStart
        from code in Try(CodeBlockCode).Optional()
        from end in CodeBlockEnd
        select new CodeBlockMarkup(code.GetValueOrDefault(""), language.TrimEnd());

    // Url
    private static readonly Parser<char, UrlMarkup> Url =
        UrlChar.AtLeastOnceString()
            .Where(s => UrlRegex.IsMatch(s))
            .Select(s => new UrlMarkup(s));

    // Preformatted text
    private static readonly Parser<char, PreformattedTextMarkup> PreformattedText =
        CodeBlockToken.Before(NotPreChar.OrEnd()).Unexpected()
            .Then(NotPreChar.Or(Try(QuotedPreToken)).ManyString().Between(PreToken))
            .Select(s => new PreformattedTextMarkup(s));

    // Plain text
    private static readonly Parser<char, PlainTextMarkup> PlainText =
        NotTokenChar.AtLeastOnceString().Select(s => new PlainTextMarkup(s));

    // Basic text
    private static readonly Parser<char, TextMarkup> BasicText =
        Try(TryOneOf(
                Mention.Cast<TextMarkup>(),
                PreformattedText.Cast<TextMarkup>(),
                Url.Cast<TextMarkup>(),
                PlainText.Cast<TextMarkup>()
            ))
            .AtLeastOnce()
            .Select(seq => {
                var list = seq.ToList();
                return list.Count == 1 ? list[0] : new TextMarkupSeq(list.ToArray());
            });

    // Stylized text
    private static readonly Parser<char, TextMarkup> BoldText =
        Try(BasicText).Or(Rec(() => Text)).Between(BoldToken)
            .Select(t => (TextMarkup)new StylizedTextMarkup(t, TextStyle.Bold));
    private static readonly Parser<char, TextMarkup> ItalicText =
        Try(BasicText).Or(Rec(() => Text)).Between(ItalicToken)
            .Select(t => (TextMarkup)new StylizedTextMarkup(t, TextStyle.Italic));

    // Text
    private static readonly Parser<char, TextMarkup> Text =
        Try(TryOneOf(BoldText, ItalicText, BasicText))
            .AtLeastOnce()
            .Select(seq => {
                var list = seq.ToList();
                return list.Count == 1 ? list[0] : new TextMarkupSeq(list.ToArray());
            });

    // Markup sequence
    private static readonly Parser<char, Markup> Markup =
        Try(TryOneOf(Text.Cast<Markup>(), CodeBlock.Cast<Markup>()))
            .AtLeastOnce()
            .Select(seq => {
                var list = seq.ToList();
                return list.Count == 1 ? list[0] : new MarkupSeq(list.ToArray());
            });

    // Unparsed markup
    private static readonly Parser<char, Markup> UnparsedMarkup =
        TokenChar.AtLeastOnceString().Select(s => (Markup)new UnparsedMarkup(s));

    // Markup + unparsed markup sequence
    private static readonly Parser<char, Markup> MaybeMarkup =
        Try(TryOneOf(Markup, UnparsedMarkup))
            .AtLeastOnce()
            .Select(seq => {
                var list = seq.ToList();
                return list.Count == 1 ? list[0] : new MarkupSeq(ImmutableArray.Create(list.ToArray()));
            });

    public static Markup Parse(string text)
    {
        var result = MaybeMarkup.Parse(text);
        return result.Success ? result.Value : new PlainTextMarkup(text);
    }
}
