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
    public bool AllowIncompleteMarkup { get; init; }

    public Markup Parse(string text)
    {
        var markup = ParseRaw(text, UseUnparsedTextMarkup, AllowIncompleteMarkup);
        if (MustSimplify)
            markup = markup.Simplify();
        return markup;
    }

    public static Markup ParseRaw(
        string text,
        bool useUnparsedTextMarkup = false,
        bool allowIncompleteMarkup = false)
    {
        if (text.IsNullOrEmpty())
            return EmptyResult;

        // The grammar sees a single line ending style, so any input produces the same markup.
        // Without this a lone '\r' ends the parse early and silently truncates the message.
        text = text.NormalizeNewLines(NewLineMarkup.Instance.Text);
        var parser = (useUnparsedTextMarkup, allowIncompleteMarkup) switch {
            (false, false) => FullMarkup,
            (true, false) => FullWithUnparsedMarkup,
            (false, true) => IncompleteMarkup,
            (true, true) => IncompleteWithUnparsedMarkup,
        };
        var result = parser.Parse(text);
        return result.Success ? result.Value : EmptyResult;
    }

    // Character classes

    private static readonly Parser<char, char> WhitespaceChar =
        Token(c => c is not ('\r' or '\n' or '\u2028') && char.IsWhiteSpace(c)).Labelled("whitespace");
    private static readonly Parser<char, char> NotEndOfLineChar =
        Token(c => c is not ('\r' or '\n' or '\u2028')).Labelled("not line separator");
    private static readonly Parser<char, char> IdChar =
        Token(c => char.IsLetterOrDigit(c) || c is '_' or '-' or ':' or '.' or '%' or '~')
            .Labelled("letter, digit, '_', '-', ':', '.', '%', or '~'");
    private static readonly Parser<char, char> SpecialChar =
        Token(c => c is '*' or '`' or '@' or '|').Labelled("'*', '`', '@', or '|'");
    private static readonly Parser<char, char> NotSpecialOrWhitespaceChar =
        Token(c => !(char.IsWhiteSpace(c) || c is '*' or '`' or '@' or '|'))
            .Labelled("not whitespace, line separator, '_', '*', '`', '@', or '|'");

    // Tokens

    private static readonly Parser<char, TextStyle> BoldToken = String("**").WithResult(TextStyle.Bold);
    private static readonly Parser<char, TextStyle> ItalicToken = Char('*').WithResult(TextStyle.Italic);
    private static readonly Parser<char, TextStyle> SpoilerToken = String("||").WithResult(TextStyle.Spoiler);
    private static readonly Parser<char, char> PreToken = Char('`');
    private static readonly Parser<char, char> NotPreToken = Token(c => c != '`');
    private static readonly Parser<char, char> DoublePreToken = Try(PreToken.Then(PreToken));
    private static readonly Parser<char, string> CodeBlockToken = String("```");
    private static readonly Parser<char, char> AtToken = Char('@');

    // ':' and '.' only join id segments — they're consumed as part of an id solely when another id
    // char follows. This keeps a trailing ':' or '.' out of the id, so "@`Name`a:c:1: text" (the
    // author-mention prefix in a multi-message copy) and "@`Name`u:id. Next" parse the mention and
    // leave the punctuation to the surrounding text.
    private static readonly Parser<char, char> IdBodyChar =
        Token(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '%' or '~')
            .Labelled("letter, digit, '_', '-', '%', or '~'");
    private static readonly Parser<char, string> Id =
        OneOf(IdBodyChar, Try(Token(c => c is ':' or '.').Before(Lookahead(IdChar)))).AtLeastOnceString();
    private static readonly Parser<char, string> QuotedName =
        PreToken.Then(NotPreToken.Or(DoublePreToken).ManyString()).Before(PreToken);

    // Regex: Url

    private static readonly UInt128 FirstUrlCharBits;
    private static readonly Parser<char, char> FirstUrlChar =
        Token(c => FirstUrlCharBits.IsBitSet(c)).Labelled("First URL character");
    private static readonly Parser<char, char> UrlChar =
        Token(c => char.IsLetterOrDigit(c) || @":;/\?&#+=%$@*[](){}_.,\-~'!|".Contains(c)).Labelled("URL character");

    private const string UrlProtoRe = @"(http|ftp)s?\:\/\/";
    private const string UrlHostRe = @"[0-9a-zA-Z](?>[-.\w]*[0-9a-zA-Z])*";
    private const string UrlPortRe = @":(?:6553[0-5]|655[0-2][0-9]|65[0-4][0-9][0-9]|6[0-4][0-9]{3}|[1-5][0-9]{4}|[1-9][0-9]{0,3})";
    private const string UrlPathRe = @"[/?][a-zA-Z0-9\-\.\?\*\,\'\[\]\(\)\{\}\/\\\+&%\$#_!\|;=:@~]*";
    private const string FullUrlRe = $"{UrlProtoRe}{UrlHostRe}({UrlPortRe})?({UrlPathRe})?";
    private const string ShortUrlRe = $@"www\.{UrlHostRe}({UrlPortRe})?({UrlPathRe})?";
    private const string UrlRe = $"^(?:{FullUrlRe}|{ShortUrlRe})$";

    [GeneratedRegex(UrlRe, RegexOptions.ExplicitCapture)]
    private static partial Regex UrlRegexFactory();
    private static readonly Regex UrlRegex = UrlRegexFactory();

    // Regex: Email

    private static readonly UInt128 FirstEmailCharBits;
    private static readonly Parser<char, char> FirstEmailChar =
        Token(c => FirstEmailCharBits.IsBitSet(c)).Labelled("First e-mail character");
    private static readonly Parser<char, char> EmailChar =
        Token(c => char.IsLetterOrDigit(c) || ":;/?&#+=%$_.,\\-~'@".Contains(c)).Labelled("E-mail character");

    private const string EmailNameRe = @"[A-Za-z0-9!#$%&'*+\-\/=?\^_`{|}~][A-Za-z0-9!#$%&'*+\-\/=?\^_`{|}~.]*";
    private const string ShortEmailRe = $"{EmailNameRe}@{UrlHostRe}";
    private const string FullEmailRe = $"mailto:{ShortEmailRe}";
    private const string EmailRe = $"^(?:{FullEmailRe}|{ShortEmailRe})$";

    [GeneratedRegex(EmailRe, RegexOptions.ExplicitCapture)]
    private static partial Regex EmailRegexFactory();
    private static readonly Regex EmailRegex = EmailRegexFactory();

    // Markup parsers

    // Word text & delimiter
    private static readonly Parser<char, Markup> NonWhitespaceText =
        NotSpecialOrWhitespaceChar.AtLeastOnceString().ToTextMarkup(TextMarkupKind.Plain, false);
    internal static readonly Parser<char, Markup> WhitespaceText =
        WhitespaceChar.AtLeastOnceString().ToTextMarkup(TextMarkupKind.Plain, false);

    // Header level: 1 to 3 '#' chars followed by whitespace (lookahead, not consumed)
    private static readonly Parser<char, int> HeaderLevel =
        Char('#').AtLeastOnceString()
            .Where(s => s.Length is >= HeaderMarkup.MinLevel and <= HeaderMarkup.MaxLevel)
            .Select(s => s.Length)
            .Before(Lookahead(WhitespaceChar));

    // Check what follows after a newline (used in lookahead)
    private static readonly Parser<char, char> ParagraphBreakAhead =
        EndOfLine.Then(EndOfLine).ThenReturn('\n'); // double newline = paragraph break
    private static readonly Parser<char, char> ListItemAhead =
        EndOfLine.Then(OneOf(Char('-'), Char('*')).Before(WhitespaceChar));
    private static readonly Parser<char, string> CodeBlockAhead =
        EndOfLine.Then(CodeBlockToken);
    private static readonly Parser<char, int> HeaderAhead =
        EndOfLine.Then(HeaderLevel);
    private static readonly Parser<char, char> QuoteAhead =
        EndOfLine.Then(Char('>').Before(WhitespaceChar)); // "> " block-quote line start

    // Inline newline (single newline within inline content, not paragraph break or list/code/header/quote block start)
    private static readonly Parser<char, Markup> InlineNewLine =
        Lookahead(Not(ParagraphBreakAhead)) // not paragraph break
            .Then(Lookahead(Not(ListItemAhead))) // not list item start
            .Then(Lookahead(Not(CodeBlockAhead))) // not code block start
            .Then(Lookahead(Not(HeaderAhead))) // not header start
            .Then(Lookahead(Not(QuoteAhead))) // not block-quote start
            .Then(EndOfLine)
            .ThenReturn(NewLineMarkup.Instance as Markup);

    // Whitespace or newline (for inline content separators)
    internal static readonly Parser<char, Markup> WhitespaceOrNewLine =
        SafeTryOneOf(InlineNewLine, WhitespaceText);

    // Mentions
    private static Parser<char, Markup> MentionParserFactory(string name = "") =>
        from id in Id
        let mentionId = MentionRef.TryParse(id, true)
        where mentionId != null
        select (Markup)MentionMarkup.New(mentionId, name);
    private static readonly Parser<char, Markup> NamedMention =
        // @`User Name`userId
        AtToken.Then(QuotedName).Then(MentionParserFactory).Debug("@`name`");
    private static readonly Parser<char, Markup> UnnamedMention =
        // @userId
        AtToken.Then(MentionParserFactory()).Debug("@");
    private static readonly Parser<char, Markup> Mention =
        SafeTryOneOf(NamedMention, UnnamedMention);

    // Preformatted text opener - the guard keeps a ``` code block fence out of an inline code span
    private static readonly Parser<char, Pidgin.Unit> PreformattedTextStart =
        Lookahead(Not(CodeBlockToken.Before(NotPreToken.OrEnd())));
    private static readonly Parser<char, string> PreformattedTextBody =
        NotPreToken.Or(DoublePreToken).ManyString();

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

    // Fallback for single-line content: consume a run of stray '*'/'`'/'@' as plain text when no
    // styled/preformatted/mention/url markup matches. Without it, an unmatched '**' (e.g. the
    // **`a`/`b`** ambiguity) would stall list-item parsing and silently drop the rest of the message.
    private static readonly Parser<char, Markup> StraySpecialText =
        SpecialChar.AtLeastOnceString().ToTextMarkup(TextMarkupKind.Plain, false);

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
    // End of an unclosed code block: matches end-of-input as a fallback when
    // the closing ``` was not provided. The resulting value is unused.
    private static readonly Parser<char, char> CodeBlockEndOrEof =
        CodeBlockEnd.Or(End.ThenReturn(default(char)));
    private static readonly Parser<char, string> CodeBlockCode =
        Try(CodeBlockLine)
            .SeparatedAndOptionallyTerminated(Try(EndOfLine))
            .Select(lines => {
                var buffer = ArrayBuffer<string>.Lease(false);
                var sb = ActualLab.Text.StringBuilderExt.Acquire();
                try {
                    var minIndent = int.MaxValue;
                    foreach (var line in lines) {
                        var properLine = line.Replace("\t", "    "); // Replace tabs w/ spaces
                        var indentLength = properLine.GetPrefixCharCount(' ');
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
        from end in CodeBlockEndOrEof
        select (Markup)new CodeBlockMarkup(code.GetValueOrDefault(""), language.TrimEnd())
        ).Debug("<Code>");

    // Block-level pieces that don't depend on any grammar variant, so they're built once rather
    // than per InternalParsers instance. Everything downstream of TextBlock or of the unparsed
    // text markup kind does vary, and lives in InternalParsers.Build instead.

    // A single paragraph line (can be empty - 0 or more chars)
    private static readonly Parser<char, string> ParagraphLine = NotEndOfLineChar.ManyString();

    // Check if next line starts a block element (CodeBlock, ListBlock, Header, or BlockQuote)
    private static readonly Parser<char, char> BlockElementAhead =
        Lookahead(Try(CodeBlockToken.ThenReturn('`'))
            .Or(Try(OneOf(Char('-'), Char('*')).Before(WhitespaceChar)))
            .Or(Try(HeaderLevel.ThenReturn('#')))
            .Or(Try(Char('>').Before(WhitespaceChar))));

    private static readonly Parser<char, string> QuoteContentLine =
        Char('>').Then(WhitespaceChar).Then(ParagraphLine);

    private static readonly Parser<char, Markup> FullMarkup =
        new InternalParsers(false).Build();
    private static readonly Parser<char, Markup> FullWithUnparsedMarkup =
        new InternalParsers(true).Build();
    private static readonly Parser<char, Markup> IncompleteMarkup =
        new IncompleteInternalParsers(false).Build();
    private static readonly Parser<char, Markup> IncompleteWithUnparsedMarkup =
        new IncompleteInternalParsers(true).Build();

    // Type initializer
    static MarkupParser()
    {
        for (var c = (char)0; c < 256; c++) {
            if (char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-/=?^_`{|}~".Contains(c))
                FirstEmailCharBits.SetBit(c);
            if (c is 'h' // for http:// and https://
                or 'f' // for ftp://
                or 'w' // for www.
               )
                FirstUrlCharBits.SetBit(c);
        }
    }

    // Nested types
    private class InternalParsers(bool useUnparsedTextMarkup)
    {
        private bool UseUnparsedTextMarkup { get; } = useUnparsedTextMarkup;

        // Assigned by Build before any parsing, and read through Rec(...) so the stylized parsers
        // can recurse back into the text block that contains them.
        protected Parser<char, Markup> TextBlock { get; private set; } = null!;

        public Parser<char, Markup> Build()
        {
            var textMarkupKind = UseUnparsedTextMarkup ? TextMarkupKind.Unparsed : TextMarkupKind.Plain;

            var preformattedText = CreatePreformattedText();
            var nonStylizedMarkup = SafeTryOneOf(Mention, preformattedText, Url, NonWhitespaceText).Debug("T");
            var boldMarkup = CreateStylized(Try(BoldToken), TextStyle.Bold).Debug("**");
            var italicMarkup = CreateStylized(ItalicToken, TextStyle.Italic).Debug("*");
            var spoilerMarkup = CreateStylized(Try(SpoilerToken), TextStyle.Spoiler).Debug("||");

            // Text block for single-line content (list items) - no newlines allowed
            var textBlockSingleLine =
                SafeTryOneOf(boldMarkup, italicMarkup, spoilerMarkup, nonStylizedMarkup, StraySpecialText)
                    .AtLeastOnceSingleLineMarkup()
                    .Debug("<TextSingleLine>");

            // Text block (includes inline newlines for multi-line styled text in paragraphs)
            TextBlock =
                SafeTryOneOf(boldMarkup, italicMarkup, spoilerMarkup, nonStylizedMarkup, InlineNewLine)
                    .AtLeastOnceInlineMarkup()
                    .Debug("<Text>");

            // List block (list items use single-line text block - no newlines within items)
            var unorderedListItem =
                from _ in OneOf(Char('-'), Char('*')).Before(WhitespaceChar)
                from content in textBlockSingleLine.ManyMarkup()
                select (Markup)new ListItemMarkup(content);
            var listBlock = (
                from first in unorderedListItem
                from rest in Try(EndOfLine.Then(unorderedListItem)).Many()
                select (Markup)new ListMarkup(
                    new[] { first }.Concat(rest).Select(c => (ListItemMarkup)c).ToArray())
                ).Debug("<List>");
            // Explicitly typed: the query yields TextMarkup, and SafeTryOneOf below needs Markup
            Parser<char, Markup> unparsedTextBlock = (
                from whitespace in WhitespaceString
                from special in SpecialChar.AtLeastOnceString()
                select TextMarkup.New(textMarkupKind, whitespace + special, true)
                ).Debug("<Unparsed>");

            var inlineParser =
                SafeTryOneOf(InlineNewLine, WhitespaceText, TextBlock, unparsedTextBlock).Debug("<InlineElement>")
                    .ManyMarkup().Debug("<Inline>");

            // Paragraph: collect all content until paragraph break or block element, then parse as inline
            // This allows styled text (**bold**) to span multiple lines
            var paragraph = (
                from firstLine in ParagraphLine
                from restParts in Try(
                    from nl in EndOfLine
                    from _ in Lookahead(Not(EndOfLine)) // not empty line (paragraph break)
                    from __ in Lookahead(Not(BlockElementAhead)) // not block element ahead
                    from line in ParagraphLine
                    select "\n" + line // preserve newline in content
                ).Many()
                select BuildParagraph(firstLine, restParts, inlineParser)
                ).Debug("<Paragraph>");

            // Header: 1-3 '#' followed by whitespace and a single line of inline content
            var header = (
                from level in HeaderLevel
                from _ in WhitespaceChar.AtLeastOnce()
                from line in ParagraphLine
                select BuildHeader(level, line.TrimEnd(), inlineParser)
                ).Debug("<Header>");

            // Block quote: one or more consecutive "> " lines; inner content is inline markup
            // (mentions/styles/urls/emoji/newlines) — no nested block elements like code blocks.
            var blockquote = (
                from firstLine in QuoteContentLine
                from restLines in Try(
                    from nl in EndOfLine
                    from line in QuoteContentLine
                    select "\n" + line
                ).Many()
                select BuildBlockQuote(firstLine, restLines, inlineParser)
                ).Debug("<BlockQuote>");

            // Any standalone block (list/code/header/quote, or paragraph including empty/inline-only).
            var blockOrHeader = SafeTryOneOf(CodeBlock, listBlock, header, blockquote);
            var block = SafeTryOneOf(blockOrHeader, paragraph);

            // After the first block, every subsequent block is preceded by one or more
            // newlines. We capture the count and let BuildBlockSequence translate
            // (leadingNewlines − minSep) extra newlines into ParagraphMarkup.Empty
            // entries — one per blank line beyond the minimum block-boundary separator.
            var separatorAndNextBlock =
                from nls in Try(EndOfLine).AtLeastOnce()
                from item in block
                select (nls.Count(), item);

            return from first in block
                from rest in Try(separatorAndNextBlock).Many()
                select BuildBlockSequence(first, rest);
        }

        // Protected methods

        protected virtual Parser<char, Markup> CreatePreformattedText()
            => PreformattedTextStart
                .Then(PreformattedTextBody.Between(PreToken))
                .Select(s => (Markup)new PreformattedTextMarkup(s))
                .Debug("`");

        protected virtual Parser<char, Markup> CreateStylized(Parser<char, TextStyle> token, TextStyle style)
            => Rec(() => TextBlock).Between(token)
                .Select(t => (Markup)new StylizedMarkup(t, style));

        // Private methods

        private static Markup BuildBlockSequence(
            Markup first,
            IEnumerable<(int LeadingNewlines, Markup Item)> rest)
        {
            var items = new List<Markup> { first };
            Markup prev = first;
            foreach (var (leadingNewlines, item) in rest) {
                var prevIsNonEmptyPara = MarkupSeqFormatHelper.IsNonEmptyPara(prev);
                var currIsNonEmptyPara = MarkupSeqFormatHelper.IsNonEmptyPara(item);
                var currIsEmptyPara = MarkupSeqFormatHelper.IsEmptyPara(item);

                int minSep;
                if (currIsEmptyPara)
                    // For a trailing/intervening empty paragraph the EmptyPara itself
                    // already accounts for one newline; pair it with the paragraph
                    // break when the predecessor is a non-empty paragraph, otherwise
                    // a single block boundary newline is enough.
                    minSep = prevIsNonEmptyPara ? 2 : 1;
                else if (prevIsNonEmptyPara && currIsNonEmptyPara)
                    // Adjacent non-empty paragraphs require a paragraph break.
                    minSep = 2;
                else
                    // Any other transition needs exactly one block boundary newline.
                    minSep = 1;

                var emptyParas = Math.Max(0, leadingNewlines - minSep);
                for (var i = 0; i < emptyParas; i++) {
                    items.Add(ParagraphMarkup.Empty);
                    prev = ParagraphMarkup.Empty;
                }
                items.Add(item);
                prev = item;
            }
            return Markup.Join(items);
        }

        private static Markup BuildParagraph(string firstLine, IEnumerable<string> restParts, Parser<char, Markup> inlineParser)
        {
            // Concatenate all content (restParts already include newlines)
            var content = firstLine + string.Concat(restParts);
            return new ParagraphMarkup(inlineParser.ParseOrThrow(content));
        }

        private static Markup BuildHeader(int level, string line, Parser<char, Markup> inlineParser)
            => new HeaderMarkup(level, inlineParser.ParseOrThrow(line));

        private static Markup BuildBlockQuote(string firstLine, IEnumerable<string> restLines, Parser<char, Markup> inlineParser)
        {
            var content = firstLine + string.Concat(restLines);
            return new BlockQuoteMarkup(inlineParser.ParseOrThrow(content));
        }
    }

    private sealed class IncompleteInternalParsers(bool useUnparsedTextMarkup)
        : InternalParsers(useUnparsedTextMarkup)
    {
        // Protected methods

        protected override Parser<char, Markup> CreatePreformattedText()
            => SafeTryOneOf(
                base.CreatePreformattedText(),
                PreformattedTextStart
                    .Then(PreToken)
                    .Then(PreformattedTextBody)
                    .Before(End)
                    .Select(s => (Markup)new PreformattedTextMarkup(s) { IsIncomplete = true })
                    .Debug("`?"));

        protected override Parser<char, Markup> CreateStylized(Parser<char, TextStyle> token, TextStyle style)
        {
            // A two-character closing token can be half-arrived as well ("**bold*", "||secret|").
            // Consuming that half keeps the span matched; without it the span degrades to literal
            // text, which for a spoiler means showing what it is meant to hide.
            var end = style switch {
                TextStyle.Bold => Char('*').Optional().Then(End),
                TextStyle.Spoiler => Char('|').Optional().Then(End),
                _ => End,
            };
            // The content must be non-empty: an empty match would let a nested span consume the
            // closing token of the span that encloses it, turning "**bold**" into an incomplete
            // bold wrapping an incomplete empty one.
            return SafeTryOneOf(
                base.CreateStylized(token, style),
                token
                    .Then(Rec(() => TextBlock))
                    .Before(end)
                    .Select(t => (Markup)new StylizedMarkup(t, style) { IsIncomplete = true })
                    .Debug("?"));
        }
    }
}
