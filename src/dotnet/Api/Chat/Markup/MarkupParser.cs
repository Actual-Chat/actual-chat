using System.Text.RegularExpressions;
using Pidgin;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;
using static ActualChat.Chat.ParserExt;

namespace ActualChat.Chat;

/// <summary>
/// Parses text into <see cref="Markup"/> using Pidgin parser combinators.
/// </summary>
#pragma warning disable CA1823 // Unused field ...

public partial class MarkupParser : IMarkupParser
{
    public static Markup EmptyResult => Markup.EmptyParagraph;

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
            return EmptyResult;

        var parser = useUnparsedTextMarkup ? FullWithUnparsedMarkup : FullMarkup;
        var result = parser.Parse(text);
        return result.Success ? result.Value : EmptyResult;
    }

    // Character classes

    private static readonly Parser<char, char> WhitespaceChar =
        Token(c => c is not ('\r' or '\n' or '\u2028') && char.IsWhiteSpace(c)).Labelled("whitespace");
    private static readonly Parser<char, char> NotEndOfLineChar =
        Token(c => c is not ('\r' or '\n' or '\u2028')).Labelled("not line separator");
    private static readonly Parser<char, char> IdChar =
        Token(c => char.IsLetterOrDigit(c) || c is '_' or '-' or ':').Labelled("letter, digit, '_', '-', or ':'");
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

    // Regex: Url

    private static readonly UInt128 FirstUrlCharBits;
    private static readonly Parser<char, char> FirstUrlChar =
        Token(c => FirstUrlCharBits.IsBitSet(c)).Labelled("First URL character");
    private static readonly Parser<char, char> UrlChar =
        Token(c => char.IsLetterOrDigit(c) || @":;/\?&#+=%$@*[](){}_.,\-~'!|".OrdinalContains(c)).Labelled("URL character");

    private const string UrlProtoRe = @"(http|ftp)s?\:\/\/";
    private const string UrlHostRe = @"[0-9a-zA-Z]([-.\w]*[0-9a-zA-Z])*";
    private const string UrlPortRe = @":(0-9)*";
    private const string UrlPathRe = @"\/[a-zA-Z0-9\-\.\?\*\,\'\[\]\(\)\{\}\/\\\+&%\$#_!\|]*";
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

    // Check what follows after a newline (used in lookahead)
    private static readonly Parser<char, char> ParagraphBreakAhead =
        EndOfLine.Then(EndOfLine).ThenReturn('\n'); // double newline = paragraph break
    private static readonly Parser<char, char> ListItemAhead =
        EndOfLine.Then(OneOf(Char('-'), Char('*')).Before(WhitespaceChar));
    private static readonly Parser<char, string> CodeBlockAhead =
        EndOfLine.Then(CodeBlockToken);

    // Inline newline (single newline within inline content, not paragraph break or list/code block start)
    private static readonly Parser<char, Markup> InlineNewLine =
        Lookahead(Not(ParagraphBreakAhead)) // not paragraph break
            .Then(Lookahead(Not(ListItemAhead))) // not list item start
            .Then(Lookahead(Not(CodeBlockAhead))) // not code block start
            .Then(EndOfLine)
            .ThenReturn(NewLineMarkup.Instance as Markup);

    // Whitespace or newline (for inline content separators)
    internal static readonly Parser<char, Markup> WhitespaceOrNewLine =
        SafeTryOneOf(InlineNewLine, WhitespaceText);

    // Mentions
    private static Parser<char, Markup> MentionParserFactory(string name = "") =>
        from id in Id
        let mentionId = MentionId.TryParse(id, true)
        where mentionId != null
        select (Markup)new MentionMarkup(mentionId, name);
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
        .Select(s => (Markup)new UrlMarkup(s, UrlMarkupKind.Www));
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

    // Text block for single-line content (list items) - no newlines allowed
    private static readonly Parser<char, Markup> TextBlockSingleLine =
        SafeTryOneOf(BoldMarkup, ItalicMarkup, NonStylizedMarkup)
            .AtLeastOnceSingleLineMarkup()
            .Debug("<TextSingleLine>");

    // Text block (includes inline newlines for multi-line styled text in paragraphs)
    private static readonly Parser<char, Markup> TextBlock =
        SafeTryOneOf(BoldMarkup, ItalicMarkup, NonStylizedMarkup, InlineNewLine)
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
                var sb = ActualLab.Text.StringBuilderExt.Acquire();
                try {
                    var minIndent = int.MaxValue;
                    foreach (var line in lines) {
                        var properLine = line.OrdinalReplace("\t", "    "); // Replace tabs w/ spaces
                        var indentLength = properLine.GetIndentLength();
                        if (indentLength == properLine.Length)
                            properLine = ""; // Empty line
                        else
                            minIndent = Math.Min(minIndent, indentLength);
                        buffer.Add(properLine);
                    }
                    if (buffer.Count == 0)
                        return "";

                    var isFirst = true;
                    foreach (var line in buffer) {
                        if (!isFirst)
                            sb.Append("\r\n"); // We want stable line endings here
                        isFirst = false;
                        sb.Append(minIndent < line.Length ? line[minIndent..] : "");
                    }
                    return sb.ToString(); // Ok here, .Release() is in finally block
                }
                finally {
                    sb.Release();
                    buffer.Release();
                }
            });
    private static readonly Parser<char, Markup> CodeBlock = (
        from language in TryOneOf(CodeBlockWithLanguageStart, CodeBlockWithoutLanguageStart)
        from code in Try(CodeBlockCode).Optional()
        from end in CodeBlockEnd
        select (Markup)new CodeBlockMarkup(code.GetValueOrDefault(""), language.TrimEnd())
        ).Debug("<Code>");

    // List block (list items use single-line text block - no newlines within items)
    private static readonly Parser<char, Markup> UnorderedListItem =
        from _ in OneOf(Char('-'), Char('*')).Before(WhitespaceChar)
        from content in TextBlockSingleLine.ManyMarkup()
        select (Markup)new ListItemMarkup(content);
    private static readonly Parser<char, Markup> ListBlock = (
        from first in UnorderedListItem
        from rest in Try(EndOfLine.Then(UnorderedListItem)).Many()
        select (Markup)new ListMarkup(
            new[] { first }.Concat(rest).Select(c => (ListItemMarkup)c).ToArray())
        ).Debug("<List>");

    private static readonly Parser<char, Markup> FullWithUnparsedMarkup =
        new InternalParsers(true).Parser;

    private static readonly Parser<char, Markup> FullMarkup =
        new InternalParsers(false).Parser;

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

    // Nested types
    private class InternalParsers
    {
        public readonly Parser<char, Markup> Parser;

        public InternalParsers(bool useUnparsedTextMarkup)
        {
            var textMarkupKind = useUnparsedTextMarkup ? TextMarkupKind.Unparsed : TextMarkupKind.Plain;
            Parser<char, Markup> unparsedTextBlock = (
                from whitespace in WhitespaceString
                from special in SpecialChar.AtLeastOnceString()
                select TextMarkup.New(textMarkupKind, whitespace + special, true)
                ).Debug("<Unparsed>");

            Parser<char, Markup> inlineParser =
                SafeTryOneOf(InlineNewLine, WhitespaceText, TextBlock, unparsedTextBlock).Debug("<InlineElement>")
                    .ManyMarkup().Debug("<Inline>");

            // A single paragraph line (can be empty - 0 or more chars)
            Parser<char, string> paragraphLine = NotEndOfLineChar.ManyString();

            // Check if next line starts a block element (CodeBlock or ListBlock)
            Parser<char, char> blockElementAhead =
                Lookahead(Try(CodeBlockToken.ThenReturn('`'))
                    .Or(Try(OneOf(Char('-'), Char('*')).Before(WhitespaceChar))));

            // Paragraph: collect all content until paragraph break or block element, then parse as inline
            // This allows styled text (**bold**) to span multiple lines
            Parser<char, Markup> paragraph = (
                from firstLine in paragraphLine
                from restParts in Try(
                    from nl in EndOfLine
                    from _ in Lookahead(Not(EndOfLine)) // not empty line (paragraph break)
                    from __ in Lookahead(Not(blockElementAhead)) // not block element ahead
                    from line in paragraphLine
                    select "\n" + line // preserve newline in content
                ).Many()
                select BuildParagraph(firstLine, restParts, inlineParser)
                ).Debug("<Paragraph>");

            Parser<char, Markup> block =
                SafeTryOneOf(CodeBlock, ListBlock, paragraph);

            // Empty paragraph: matches when there's another newline (or end) after paragraph break
            Parser<char, Markup> emptyParagraph =
                Lookahead(Try(EndOfLine).Or(End.ThenReturn(""))).ThenReturn((Markup)new ParagraphMarkup(Markup.EmptyText));

            // After code/list block: single newline can be followed by any block
            // After paragraph: need double newline for next paragraph, single newline OK for code/list
            // Solution: Try all options with proper backtracking
            Parser<char, Markup> blockSeparatorThenBlock =
                Try(EndOfLine.Then(SafeTryOneOf(CodeBlock, ListBlock))) // single \n + code/list
                    .Or(Try(EndOfLine.Then(EndOfLine).Then(SafeTryOneOf(emptyParagraph, block)))) // \n\n + empty or block
                    .Or(EndOfLine.Then(paragraph)); // single \n + paragraph (only works after code/list)

            Parser =
                from first in block
                from rest in Try(blockSeparatorThenBlock).Many()
                select Markup.Join(new[] { first }.Concat(rest));
        }

        private static Markup BuildParagraph(string firstLine, IEnumerable<string> restParts, Parser<char, Markup> inlineParser)
        {
            // Concatenate all content (restParts already include newlines)
            var content = firstLine + string.Concat(restParts);
            return new ParagraphMarkup(inlineParser.ParseOrThrow(content));
        }
    }
}
