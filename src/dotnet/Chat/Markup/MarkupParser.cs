using System.Text.RegularExpressions;
using Cysharp.Text;
using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;
using static ActualChat.Chat.ParserExt;

namespace ActualChat.Chat;

public class MarkupParser : IMarkupParser
{
    public bool UseUnparsedTextMarkup { get; init; }
    public bool MustSimplify { get; init; } = true;

    public Markup Parse(string text)
    {
        var markup = ParseRaw(text, UseUnparsedTextMarkup);
        if (MustSimplify)
            markup = markup.Simplify();
        return markup;
    }

    // Character classes

    private static readonly Parser<char, char> FirstUrlChar =
        Token(c => c is 'h' // for http:// and https://
            or 'f' // for ftp://
            or 'm' // for mailto:
            or 't' // for tel:
            or 'w' // for www.
            ).Labelled("First URL character");
    private static readonly Parser<char, char> UrlChar =
        Token(c => char.IsLetterOrDigit(c) || ":;/?&#+=%_.\\-~".OrdinalContains(c)).Labelled("URL character");
    private static readonly Parser<char, char> WhitespaceChar =
        Token(c => c is not ('\r' or '\n' or '\u2028') && char.IsWhiteSpace(c)).Labelled("whitespace");
    private static readonly Parser<char, char> EndOfLineChar =
        Token(c => c is '\r' or '\n' or '\u2028').Labelled("line separator");
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
    private static readonly Parser<char, char> NotSpecialOrWhitespaceChar =
        Token(c => !(char.IsWhiteSpace(c) || c is '_' or '*' or '`' or '@'))
            .Labelled("not whitespace, line separator, '_', '*', '`', or '@'");

    // Tokens

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
    private static readonly Parser<char, Markup> NonWhitespaceText =
        NotSpecialOrWhitespaceChar.AtLeastOnceString().ToPlainTextMarkup();
    internal static readonly Parser<char, Markup> WhitespaceText =
        WhitespaceChar.AtLeastOnceString().ToPlainTextMarkup();
    private static readonly Parser<char, Markup> WhitespaceOrEndOfLineText =
        Whitespace.AtLeastOnceString().ToPlainTextMarkup();

    // Mention
    public static readonly Parser<char, Markup> Mention =
        AtToken.Then(TryOneOf(
            String("a:").Then(AuthorId).Select(s => (Markup)new Mention(s, MentionKind.AuthorId)),
            String("u:").Then(UserId).Select(s => (Markup)new Mention(s, MentionKind.UserId)),
            UserName.Select(s => (Markup)new Mention(s))
        )).Debug("@");

    // Url
    private static readonly string ProtoRe = @"((mailto|tel):)|((http|ftp)s?\:\/\/)";
    private static readonly string HostRe = @"[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*";
    private static readonly string MaybePortRe = @"(:(0-9)*)?";
    private static readonly string MaybePathRe = @"(\/[a-zA-Z0-9\-\.\?\,\'\/\\\+&%\$#_]*)?";
    private static readonly string FullUrlRe = $@"{ProtoRe}{HostRe}{MaybePortRe}{MaybePathRe}";
    private static readonly string ShortUrlRe = $@"www\.{HostRe}{MaybePortRe}{MaybePathRe}";
    private static readonly Regex UrlRegex = new(
        $@"^({FullUrlRe})|({ShortUrlRe})$",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture);

    private static readonly Parser<char, Markup> Url = (
        from head in FirstUrlChar
        from tail in UrlChar.AtLeastOnceString()
        select head + tail)
        .Where(s => UrlRegex.IsMatch(s))
        .Select(s => (Markup)new UrlMarkup(s))
        .Debug("Url");

    // Preformatted text
    private static readonly Parser<char, Markup> PreformattedText =
        Lookahead(Not(CodeBlockToken.Before(NotPreChar.OrEnd())))
            .Then(NotPreChar.Or(Try(QuotedPreToken)).ManyString().Between(PreToken))
            .Select(s => (Markup)new PreformattedTextMarkup(s))
            .Debug("`");

    // Mention | PreformattedText | Url | WordText
    private static readonly Parser<char, Markup> NonStylizedMarkup =
        SafeTryOneOf(Mention, PreformattedText, Url, NonWhitespaceText)
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
            .Before(WhitespaceChar.SkipUntil(EndOfLine));
    private static readonly Parser<char, char> CodeBlockEnd =
        WhitespaceChar.SkipMany().Then(CodeBlockToken).Then(Lookahead(Whitespace.OrEnd()));
    private static readonly Parser<char, string> CodeBlockLine =
        Lookahead(Not(CodeBlockEnd))
            .Then(NotEndOfLineChar.ManyString());
    private static readonly Parser<char, string> CodeBlockCode =
        Try(CodeBlockLine)
            .SeparatedAndTerminated(Try(EndOfLine))
            .Select(lines => {
                using var sb = ZString.CreateStringBuilder();
                foreach (var line in lines) {
                    sb.Append(line);
                    sb.Append("\r\n"); // We want stable line endings here
                }
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
    private static readonly Parser<char, Markup> UnparsedTextBlock = (
        from whitespace in WhitespaceString
        from special in SpecialChar.AtLeastOnceString()
        select (Markup)new UnparsedTextMarkup(whitespace + special)
        ).Debug("<Unparsed>");
    private static readonly Parser<char, Markup> UnparsedTextAsPlainTextBlock = (
        from whitespace in WhitespaceString
        from special in SpecialChar.AtLeastOnceString()
        select (Markup)new PlainTextMarkup(whitespace + special) // Plain text instead of UnparsedMarkup
        ).Debug("<Unparsed>");

    // Full markup
    private static readonly Parser<char, Markup> FullWithUnparsedMarkup =
        SafeTryOneOf(WhitespaceBlock, TextBlock, CodeBlock, UnparsedTextBlock).ManyMarkup();
    private static readonly Parser<char, Markup> FullMarkup =
        SafeTryOneOf(WhitespaceBlock, TextBlock, CodeBlock, UnparsedTextAsPlainTextBlock).ManyMarkup();

    public static Markup ParseRaw(string text, bool useUnparsedTextMarkup = false)
    {
        var parser = useUnparsedTextMarkup ? FullWithUnparsedMarkup : FullMarkup;
        var result = parser.Parse(text);
        if (!result.Success)
            return Markup.Empty;
        return NewLineRewriter.Instance.Rewrite(result.Value);
    }
}
