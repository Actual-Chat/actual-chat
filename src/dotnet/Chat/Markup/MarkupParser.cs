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

    // Regex: Image
    [GeneratedRegex("\\.(jpg|jpeg|png|gif|png|webp)$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
    private static partial Regex ImageUrlRegexFactory();
    private static readonly Regex ImageUrlRegex = ImageUrlRegexFactory();

    // Regex: Url

    private static readonly UInt128 FirstUrlCharBits;
    private static readonly Parser<char, char> FirstUrlChar =
        Token(c => FirstUrlCharBits.IsBitSet(c)).Labelled("First URL character");
    private static readonly Parser<char, char> UrlChar =
        Token(c => char.IsLetterOrDigit(c) || ":;/?&#+=%$_.,\\-~'".OrdinalContains(c)).Labelled("URL character");

    private const string UrlProtoRe = @"(http|ftp)s?\:\/\/";
    private const string UrlHostRe = @"[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*";
    private const string UrlPortRe = @":(0-9)*";
    private const string UrlPathRe = @"\/[a-zA-Z0-9\-\.\?\,\'\/\\\+&%\$#_]*";
    private const string FullUrlRe = $"{UrlProtoRe}{UrlHostRe}({UrlPortRe})?({UrlPathRe})?";
    private const string ShortUrlRe = $@"www\.{UrlHostRe}({UrlPortRe})?({UrlPathRe})?";
    private const string UrlRe = $"^({FullUrlRe})|({ShortUrlRe})$";

    [GeneratedRegex(UrlRe, RegexOptions.ExplicitCapture)]
    private static partial Regex UrlRegexFactory();
    private static readonly Regex UrlRegex = UrlRegexFactory();

    // Regex: Email

    private static readonly UInt128 FirstEmailCharBits;
    private static readonly Parser<char, char> FirstEmailChar =
        Token(c => FirstEmailCharBits.IsBitSet(c)).Labelled("First e-mail character");
    private static readonly Parser<char, char> EmailChar =
        Token(c => char.IsLetterOrDigit(c) || ":;/?&#+=%$_.,\\-~'@".OrdinalContains(c)).Labelled("E-mail character");

    private const string EmailNameRe = @"[A-Za-z0-9!#$%&'*+\-\/=?\^_`{|}~][A-Za-z0-9!#$%&'*+\-\/=?\^_`{|}~.]*";
    private const string ShortEmailRe = $"{EmailNameRe}@{UrlHostRe}";
    private const string FullEmailRe = $"mailto:{ShortEmailRe}";
    private const string EmailRe = $"^({FullEmailRe})|({ShortEmailRe})$";

    [GeneratedRegex(EmailRe, RegexOptions.ExplicitCapture)]
    private static partial Regex EmailRegexFactory();
    private static readonly Regex EmailRegex = EmailRegexFactory();

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

    // Preformatted text
    private static readonly Parser<char, Markup> PreformattedText =
        Lookahead(Not(CodeBlockToken.Before(NotPreToken.OrEnd())))
            .Then(NotPreToken.Or(DoublePreToken).ManyString().Between(PreToken))
            .Select(s => (Markup)new PreformattedTextMarkup(s))
            .Debug("`");

    // Url
    private static Parser<char, Markup> WwwUrl => (
        from head in FirstUrlChar
        from tail in UrlChar.AtLeastOnceString()
        select head + tail)
        .Where(s => UrlRegex.IsMatch(s))
        .Select(s => (Markup)new UrlMarkup(s, ImageUrlRegex.IsMatch(s) ? UrlMarkupKind.Image : UrlMarkupKind.Www));
    private static Parser<char, Markup> Email => (
        from head in FirstEmailChar
        from tail in EmailChar.AtLeastOnceString()
        select head + tail)
        .Where(s => EmailRegex.IsMatch(s))
        .Select(s => (Markup)new UrlMarkup(s, UrlMarkupKind.Email));
    private static readonly Parser<char, Markup> Url =
        SafeTryOneOf(WwwUrl, Email).Debug("Url");

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
    private static readonly Parser<char, string> CodeBlockWithLanguageStart =
        CodeBlockToken.Then(IdChar.ManyString().Before(EndOfLine)); // Language
    private static readonly Parser<char, string> CodeBlockWithoutLanguageStart =
        CodeBlockToken.ThenReturn(""); // Language
    private static readonly Parser<char, char> CodeBlockEnd =
        WhitespaceChar.SkipMany().Then(CodeBlockToken).Then(Lookahead(Whitespace.OrEnd()));
    private static readonly Parser<char, string> CodeBlockLine =
        Lookahead(Not(CodeBlockEnd))
            .Then(NotEndOfLineChar.ManyString());
    private static readonly Parser<char, string> CodeBlockCode =
        Try(CodeBlockLine)
            .SeparatedAndTerminated(Try(EndOfLine))
            .Select(lines => {
                var buffer = ArrayBuffer<string>.Lease(false);
                var sb = ZString.CreateStringBuilder();
                try {
                    var minIndent = int.MaxValue;
                    foreach (var line in lines) {
                        var properLine = line.Replace("\t", "    "); // Replace tabs w/ spaces
                        var indentLength = properLine.GetIndentLength();
                        if (indentLength == properLine.Length)
                            properLine = ""; // Empty line
                        else
                            minIndent = Math.Min(minIndent, indentLength);
                        buffer.Add(properLine);
                    }
                    if (buffer.Count == 0)
                        return "";

                    foreach (var line in buffer) {
                        sb.Append(minIndent < line.Length ? line[minIndent..] : "");
                        sb.Append("\r\n"); // We want stable line endings here
                    }
                    return sb.ToString();
                }
                finally {
                    sb.Dispose();
                    buffer.Release();
                }
            });
    private static readonly Parser<char, Markup> CodeBlock = (
        from language in TryOneOf(CodeBlockWithLanguageStart, CodeBlockWithoutLanguageStart)
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

    // Type initializer
    static MarkupParser()
    {
        for (var c = (char)0; c < 256; c++) {
            if (char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-/=?^_`{|}~".OrdinalContains(c))
                FirstEmailCharBits.SetBit(c);
            if (c is 'h' // for http:// and https://
                or 'f' // for ftp://
                or 'w' // for www.
               )
                FirstUrlCharBits.SetBit(c);
        }
    }
}
