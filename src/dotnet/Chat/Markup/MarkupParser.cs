using System.Text.RegularExpressions;
using Cysharp.Text;
using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;
using static ActualChat.Chat.ParserExt;

namespace ActualChat.Chat;

public partial class MarkupParser : IMarkupParser
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

    public static Markup ParseRaw(string text, bool useUnparsedTextMarkup = false)
    {
        if (text.IsNullOrEmpty())
            return Markup.Empty;

        var parser = useUnparsedTextMarkup ? FullWithUnparsedMarkup : FullMarkup;
        var result = parser.Parse(text);
        return result.Success ? result.Value : Markup.Empty;
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
        Token(c => char.IsLetterOrDigit(c) || ":;/?&#+=%_.,\\-~'".OrdinalContains(c)).Labelled("URL character");
    private static readonly Parser<char, char> WhitespaceChar =
        Token(c => c is not ('\r' or '\n' or '\u2028') && char.IsWhiteSpace(c)).Labelled("whitespace");
    private static readonly Parser<char, char> EndOfLineChar =
        Token(c => c is '\r' or '\n' or '\u2028').Labelled("line separator");
    private static readonly Parser<char, char> NotEndOfLineChar =
        Token(c => c is not ('\r' or '\n' or '\u2028')).Labelled("not line separator");
    private static readonly Parser<char, char> IdChar =
        Token(c => char.IsLetterOrDigit(c) || c is '_' or '-' or ':').Labelled("letter, digit, '_', '-', or ':'");
    private static readonly Parser<char, char> WordChar =
        Token(c => char.IsLetterOrDigit(c) || c is '_').Labelled("letter, digit, or '_'");
    private static readonly Parser<char, char> NotWordChar =
        Token(c => !(char.IsLetterOrDigit(c) || c is '_')).Labelled("not letter, digit, or '_'");
    private static readonly Parser<char, char> SpecialChar =
        Token(c => c is '*' or '`' or '@').Labelled("'*', '`', or '@'");
    private static readonly Parser<char, char> NotSpecialOrWhitespaceChar =
        Token(c => !(char.IsWhiteSpace(c) || c is '*' or '`' or '@'))
            .Labelled("not whitespace, line separator, '_', '*', '`', or '@'");

    // Tokens

    private static readonly Parser<char, TextStyle> BoldToken = String("**").WithResult(TextStyle.Bold);
    private static readonly Parser<char, TextStyle> ItalicToken = Char('*').WithResult(TextStyle.Italic);
    private static readonly Parser<char, char> PreToken = Char('`');
    private static readonly Parser<char, char> NotPreToken = Token(c => c != '`');
    private static readonly Parser<char, char> DoublePreToken = Try(PreToken.Then(PreToken));
    private static readonly Parser<char, string> CodeBlockToken = String("```");
    private static readonly Parser<char, char> AtToken = Char('@');

    private static readonly Parser<char, string> Id = IdChar.AtLeastOnceString();
    private static readonly Parser<char, string> QuotedName =
        PreToken.Then(NotPreToken.Or(DoublePreToken).ManyString()).Before(PreToken);

    // Markup parsers

    // Word text & delimiter
    private static readonly Parser<char, Markup> NonWhitespaceText =
        NotSpecialOrWhitespaceChar.AtLeastOnceString().ToTextMarkup(TextMarkupKind.Plain, false);
    internal static readonly Parser<char, Markup> WhitespaceText =
        WhitespaceChar.AtLeastOnceString().ToTextMarkup(TextMarkupKind.Plain, false);
    private static readonly Parser<char, Markup> WhitespaceOrEndOfLineText =
        Whitespace.AtLeastOnceString().ToTextMarkup(TextMarkupKind.Plain, true);

    // Mentions
    private static Parser<char, Markup> MentionParserFactory(string name = "") =>
        from sid in Id
        let id = new MentionId(sid, ParseOrNone.Option)
        where !id.IsNone
        select (Markup)new MentionMarkup(id, name);
    private static readonly Parser<char, Markup> NamedMention =
        // @`User Name`userId
        AtToken.Then(QuotedName).Then(MentionParserFactory).Debug("@`name`");
    private static readonly Parser<char, Markup> UnnamedMention =
        // @userId
        AtToken.Then(MentionParserFactory()).Debug("@");
    private static readonly Parser<char, Markup> Mention =
        SafeTryOneOf(NamedMention, UnnamedMention);

    // Url
    private const string ProtoRe = @"((mailto|tel):)|((http|ftp)s?\:\/\/)";
    private const string HostRe = @"[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*";
    private const string MaybePortRe = @"(:(0-9)*)?";
    private const string MaybePathRe = @"(\/[a-zA-Z0-9\-\.\?\,\'\/\\\+&%\$#_]*)?";
    private const string FullUrlRe = $@"{ProtoRe}{HostRe}{MaybePortRe}{MaybePathRe}";
    private const string ShortUrlRe = $@"www\.{HostRe}{MaybePortRe}{MaybePathRe}";

    [GeneratedRegex($@"^({FullUrlRe})|({ShortUrlRe})$", RegexOptions.ExplicitCapture)]
    private static partial Regex UrlRegexFactory();

    private static readonly Regex UrlRegex = UrlRegexFactory();

    private static readonly Parser<char, Markup> Url = (
        from head in FirstUrlChar
        from tail in UrlChar.AtLeastOnceString()
        select head + tail)
        .Where(s => UrlRegex.IsMatch(s))
        .Select(s => (Markup)new UrlMarkup(s))
        .Debug("Url");

    // Preformatted text
    private static readonly Parser<char, Markup> PreformattedText =
        Lookahead(Not(CodeBlockToken.Before(NotPreToken.OrEnd())))
            .Then(NotPreToken.Or(DoublePreToken).ManyString().Between(PreToken))
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
            .Then(IdChar.ManyString().Before(EndOfLine)) // Language
            .Before(WhitespaceChar.SkipMany());
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
        WhitespaceOrEndOfLineText.Debug("<Whitespace>");

    // Unparsed block
    private static readonly Parser<char, Markup> UnparsedTextBlock = (
        from whitespace in WhitespaceString
        from special in SpecialChar.AtLeastOnceString()
        select TextMarkup.New(TextMarkupKind.Unparsed, whitespace + special, true)
        ).Debug("<Unparsed>");
    private static readonly Parser<char, Markup> UnparsedTextAsPlainTextBlock = (
        from whitespace in WhitespaceString
        from special in SpecialChar.AtLeastOnceString()
        select TextMarkup.New(TextMarkupKind.Plain, whitespace + special, true) // Plain text instead of UnparsedMarkup
        ).Debug("<Unparsed>");

    // Full markup
    private static readonly Parser<char, Markup> FullWithUnparsedMarkup =
        SafeTryOneOf(WhitespaceBlock, TextBlock, CodeBlock, UnparsedTextBlock).ManyMarkup();
    private static readonly Parser<char, Markup> FullMarkup =
        SafeTryOneOf(WhitespaceBlock, TextBlock, CodeBlock, UnparsedTextAsPlainTextBlock).ManyMarkup();
}
