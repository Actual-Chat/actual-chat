using System.Buffers;
using System.Text.RegularExpressions;
using Pidgin;
using Pidgin.Configuration;
using static Pidgin.Parser;
using static Pidgin.Parser<char>;
using static ActualChat.Chat.ParserExt;

namespace ActualChat.Chat;

/// <summary>
/// Parses text into <see cref="Markup"/> using Pidgin parser combinators.
/// </summary>
#pragma warning disable CA1823 // Unused field ...
public sealed partial class MarkupParser : IMarkupParser
{
    public static Markup EmptyResult => Markup.EmptyParagraph;
    public bool UseUnparsedTextMarkup { get; init; }
    public bool MustSimplify { get; init; } = true;
    public bool AllowIncompleteMarkup { get; init; }

    public Markup Parse(string text)
    {
        // The shortcut produces what Simplify() would; the raw tree splits the text per word.
        if (MustSimplify && IsPlainText(text))
            return new ParagraphMarkup(new PlainTextMarkup(text));

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
        var result = parser.Parse(text, ParserConfiguration);

        return result.Success ? result.Value : EmptyResult;
    }

    // Picked from real chats: transcribed messages never carry markup and almost all fit under it
    private const int MaxPlainTextLength = 1024;

    private static bool IsPlainText(string text)
    {
        // A single line leaves only a list/quote/header marker out of the block elements, and none
        // of the three tolerates indentation; every inline element starts with a MarkupStartChars
        // char; and a url needs a literal "://" or "www." - UrlRegex is case-sensitive.
        if (text.Length is 0 or > MaxPlainTextLength || text[0] is '-' or '>')
            return false;
        if (text.AsSpan().ContainsAny(MarkupStartChars))
            return false;

        return !text.Contains("://") && !text.Contains("www.");
    }

    // Everything Pidgin rents goes through our own pool - see ParserArrayPoolProvider
    private static readonly IConfiguration<char> ParserConfiguration =
        Pidgin.Configuration.Configuration.Default<char>()
            .WithArrayPoolProvider(ParserArrayPoolProvider.Instance);

    // Character classes

    // Everything that can start markup, plus every line separator the grammar knows - see IsPlainText
    private static readonly SearchValues<char> MarkupStartChars = SearchValues.Create("*`@|#\r\n\u2028");

    // The predicates are separate from the parsers because CharRun builds its scanners straight
    // from them - see CharRunParser for why a character run doesn't go through a combinator.
    private static readonly Func<char, bool> IsWhitespaceChar =
        c => c is not ('\r' or '\n' or '\u2028') && char.IsWhiteSpace(c);
    private static readonly Func<char, bool> IsNotEndOfLineChar = c => c is not ('\r' or '\n' or '\u2028');
    private static readonly Func<char, bool> IsIdChar =
        c => char.IsLetterOrDigit(c) || c is '_' or '-' or ':' or '.' or '%' or '~';
    private static readonly Func<char, bool> IsSpecialChar = c => c is '*' or '`' or '@' or '|';
    private static readonly Func<char, bool> IsNotSpecialOrWhitespaceChar =
        c => !(char.IsWhiteSpace(c) || c is '*' or '`' or '@' or '|');
    private static readonly Func<char, bool> IsHashChar = c => c == '#';
    private static readonly Parser<char, char> WhitespaceChar = Token(IsWhitespaceChar);
    private static readonly Parser<char, char> EndOfLineChar = Token(c => c is '\r' or '\n');
    private static readonly Parser<char, char> NotEndOfLineChar = Token(IsNotEndOfLineChar);
    private static readonly Parser<char, char> IdChar = Token(IsIdChar);
    private static readonly Parser<char, char> SpecialChar = Token(IsSpecialChar);

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
        Token(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '%' or '~');
    private static readonly Parser<char, string> Id =
        OneOf(IdBodyChar, Try(Token(c => c is ':' or '.').Before(Lookahead(IdChar)))).AtLeastOnceString();
    private static readonly Parser<char, string> QuotedName =
        PreToken.Then(NotPreToken.Or(DoublePreToken).ManyString()).Before(PreToken);

    // Regex: Url

    private static readonly UInt128 FirstUrlCharBits;
    private static readonly Parser<char, char> FirstUrlChar =
        Token(c => FirstUrlCharBits.IsBitSet(c));
    private static readonly Func<char, bool> IsUrlChar =
        c => char.IsLetterOrDigit(c) || @":;/\?&#+=%$@*[](){}_.,\-~'!|".Contains(c);

    private const string UrlProtoRe = @"(http|ftp)s?\:\/\/";
    private const string UrlHostRe = @"[0-9a-zA-Z](?>[-.\w]*[0-9a-zA-Z])*";
    private const string UrlPortRe =
        @":(?:6553[0-5]|655[0-2][0-9]|65[0-4][0-9][0-9]|6[0-4][0-9]{3}|[1-5][0-9]{4}|[1-9][0-9]{0,3})";
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
        Token(c => FirstEmailCharBits.IsBitSet(c));
    private static readonly Func<char, bool> IsEmailChar =
        c => char.IsLetterOrDigit(c) || ":;/?&#+=%$_.,\\-~'@".Contains(c);

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
        CharRun.String(IsNotSpecialOrWhitespaceChar, 1).ToTextMarkup(TextMarkupKind.Plain, false);
    internal static readonly Parser<char, Markup> WhitespaceText =
        CharRun.String(IsWhitespaceChar, 1).ToTextMarkup(TextMarkupKind.Plain, false);

    // Header level: 1 to 3 '#' chars followed by whitespace (lookahead, not consumed)
    private static readonly Parser<char, int> HeaderLevel =
        CharRun.String(IsHashChar, 1)
            .Guard(s => s.Length is >= HeaderMarkup.MinLevel and <= HeaderMarkup.MaxLevel)
            .Select(s => s.Length)
            .Before(Lookahead(WhitespaceChar));

    // Table row: a line starting with '|'. That leading '|' is required, unlike in Markdown,
    // because '|' is also the spoiler token here - a pipe-less "a | b" line stays ordinary text.
    private static readonly Parser<char, string> TableRowLine =
        Lookahead(Char(TableMarkup.CellSeparator)).Then(CharRun.String(IsNotEndOfLineChar));
    // A table begins only where a row is followed by a delimiter row of matching cell count
    // ("| --- | :-: |"), so any other line starting with '|' (e.g. "||spoiler||") stays text.
    private static readonly Parser<char, char> TableStart = (
        from headerLine in TableRowLine
        from _ in EndOfLine
        from delimiterLine in TableRowLine
        select TryParseTableAlignments(headerLine, delimiterLine) != null)
        .Guard(isTableStart => isTableStart)
        .ThenReturn(TableMarkup.CellSeparator);

    // Check if next line starts a block element (CodeBlock, ListBlock, Header, BlockQuote, or Table)
    private static readonly Parser<char, char> BlockElementAhead =
        Lookahead(Try(CodeBlockToken.ThenReturn('`'))
            .Or(Try(OneOf(Char('-'), Char('*')).Before(WhitespaceChar)))
            .Or(Try(HeaderLevel.ThenReturn('#')))
            .Or(Try(Char('>').Before(WhitespaceChar)))
            .Or(Try(TableStart)));
    // What ends an inline run at the start of a line: an empty line (i.e. a paragraph break),
    // or any block element.
    private static readonly Parser<char, char> InlineBreakAhead =
        Try(EndOfLine.ThenReturn('\n')).Or(BlockElementAhead);

    // Inline newline (single newline within inline content, not a paragraph break or a block start).
    // The newline is consumed once and the guards run after it: this parser is tried at every
    // whitespace and every element boundary, so re-parsing it per guard was the hottest thing here.
    private static readonly Parser<char, Markup> InlineNewLine =
        Lookahead(EndOfLineChar)
            .Then(EndOfLine)
            .Then(Lookahead(InlineBreakAhead.SafeNot()))
            .ThenReturn(NewLineMarkup.Instance as Markup);

    // Whitespace or newline (for inline content separators).
    // Whitespace goes first only because it's the common case - the two can't both match,
    // since WhitespaceChar excludes every line separator.
    internal static readonly Parser<char, Markup> WhitespaceOrNewLine =
        SafeTryOneOf(WhitespaceText, InlineNewLine);

    // Mentions
    private static Parser<char, Markup> MentionParserFactory(string name = "")
        => Id.Select(id => MentionRef.TryParse(id, true))
            .Guard(mentionId => mentionId != null)
            .Select(mentionId => (Markup)MentionMarkup.New(mentionId!, name));
    private static readonly Parser<char, Markup> NamedMention =
        // @`User Name`userId
        AtToken.Then(QuotedName).Then(MentionParserFactory).Debug("@`name`");
    private static readonly Parser<char, Markup> UnnamedMention =
        // @userId
        AtToken.Then(MentionParserFactory()).Debug("@");
    private static readonly Parser<char, Markup> Mention =
        SafeTryOneOf(NamedMention, UnnamedMention);

    // Hashtag: '#' + a letter or '_', then letters, digits, '_', '-'. Only matches at an element
    // boundary — a mid-word '#' (c#5, item#2) stays inside the plain-text run because '#' is not
    // a special char for NotSpecialOrWhitespaceChar. All-digit tokens (#4121) are not hashtags,
    // and adjacent tags must be whitespace-separated — the trailing guard fails on '#a#b', which
    // then backtracks into a single plain-text run.
    private const int MaxHashtagLength = 64;
    private static readonly Parser<char, char> HashtagFirstChar =
        Token(c => char.IsLetter(c) || c is '_');
    private static readonly Func<char, bool> IsHashtagChar = c => char.IsLetterOrDigit(c) || c is '_' or '-';
    private static readonly Parser<char, Markup> Hashtag = (
        from head in Char('#').Then(HashtagFirstChar)
        from tail in CharRun.String(IsHashtagChar)
        select "#" + head + tail)
        .Guard(s => s.Length <= 1 + MaxHashtagLength)
        .Before(Lookahead(Char('#').SafeNot()))
        .Select(s => (Markup)new HashtagMarkup(s))
        .Debug("#");

    // Preformatted text opener - the guard keeps a ``` code block fence out of an inline code span
    private static readonly Parser<char, Pidgin.Unit> PreformattedTextStart =
        Lookahead(CodeBlockToken.Before(NotPreToken.OrEnd()).SafeNot());
    private static readonly Parser<char, string> PreformattedTextBody =
        NotPreToken.Or(DoublePreToken).ManyString();

    // Url
    // The consumed run is matched as a span, so a word that only looks like a candidate costs
    // nothing but the scan: every alphanumeric word starts an e-mail attempt, and materializing
    // it as a string just to fail the regex was the single most expensive thing per word.
    private static readonly Parser<char, Markup> WwwUrl =
        FirstUrlChar.Then(CharRun.Skip(IsUrlChar, 1))
            .Slice((span, _) => IsUrl(span) ? new string(span) : "")
            .Guard(s => s.Length != 0)
            .Select(s => (Markup)new UrlMarkup(s, UrlMarkupKind.Www));
    private static readonly Parser<char, Markup> Email =
        FirstEmailChar.Then(CharRun.Skip(IsEmailChar, 1))
            .Slice((span, _) => IsEmail(span) ? new string(span) : "")
            .Guard(s => s.Length != 0)
            .Select(s => (Markup)new UrlMarkup(s, UrlMarkupKind.Email));
    private static readonly Parser<char, Markup> Url =
        SafeTryOneOf(WwwUrl, Email).Debug("Url");

    // Fallback for single-line content: consume a run of stray '*'/'`'/'@' as plain text when no
    // styled/preformatted/mention/url markup matches. Without it, an unmatched '**' (e.g. the
    // **`a`/`b`** ambiguity) would stall list-item parsing and silently drop the rest of the message.
    private static readonly Parser<char, Markup> StraySpecialText =
        CharRun.String(IsSpecialChar, 1).ToTextMarkup(TextMarkupKind.Plain, false);

    // Code block
    private static readonly Parser<char, string> CodeBlockWithLanguageStart =
        CodeBlockToken.Then(CharRun.String(IsIdChar).Before(EndOfLine)); // Language
    private static readonly Parser<char, string> CodeBlockWithoutLanguageStart =
        CodeBlockToken.ThenReturn(""); // Language
    private static readonly Parser<char, char> CodeBlockEnd =
        CharRun.Skip(IsWhitespaceChar).Then(CodeBlockToken).Then(Lookahead(Whitespace.OrEnd()));
    private static readonly Parser<char, string> CodeBlockLine =
        Lookahead(CodeBlockEnd.SafeNot())
            .Then(CharRun.String(IsNotEndOfLineChar));
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
    private static readonly Parser<char, string> ParagraphLine = CharRun.String(IsNotEndOfLineChar);

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

    // Private methods

    private static bool IsUrl(ReadOnlySpan<char> span)
        // The regex decides; the scan in front of it is what keeps it off every h/f/w word.
        => (span.Contains(':') || span.StartsWith("www.")) && UrlRegex.IsMatch(span);

    private static bool IsEmail(ReadOnlySpan<char> span)
        // Both branches of EmailRe require an '@', so a word without one can't match.
        => span.Contains('@') && EmailRegex.IsMatch(span);

    private static TableColumnAlignment[]? TryParseTableAlignments(string headerLine, string delimiterLine)
    {
        var cells = SplitTableRow(delimiterLine);
        if (cells.Count == 0 || SplitTableRow(headerLine).Count != cells.Count)
            return null;

        var alignments = new TableColumnAlignment[cells.Count];
        for (var i = 0; i < cells.Count; i++) {
            if (TryParseTableAlignment(cells[i]) is not { } alignment)
                return null;

            alignments[i] = alignment;
        }

        return alignments;
    }

    // A delimiter cell is one or more '-' with an optional ':' on either side; ':' alone isn't one.
    private static TableColumnAlignment? TryParseTableAlignment(string cell)
    {
        if (cell.Length == 0)
            return null;

        var hasLeftColon = cell[0] == ':';
        var hasRightColon = cell[^1] == ':';
        var start = hasLeftColon ? 1 : 0;
        var end = hasRightColon ? cell.Length - 1 : cell.Length;
        if (end <= start)
            return null;

        for (var i = start; i < end; i++)
            if (cell[i] != '-')
                return null;

        return (hasLeftColon, hasRightColon) switch {
            (true, true) => TableColumnAlignment.Center,
            (true, false) => TableColumnAlignment.Left,
            (false, true) => TableColumnAlignment.Right,
            _ => TableColumnAlignment.None,
        };
    }

    // Splits a "| a | b |" line (the leading '|' is required) into trimmed cell texts.
    // The trailing '|' is optional and never produces an extra cell; "\|" is a literal '|'.
    private static List<string> SplitTableRow(string line)
    {
        var cells = new List<string>();
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        try {
            for (var i = 1; i < line.Length; i++) {
                var c = line[i];
                if (c == '\\' && i + 1 < line.Length && line[i + 1] == TableMarkup.CellSeparator) {
                    sb.Append(TableMarkup.CellSeparator);
                    i++;
                }
                else if (c == TableMarkup.CellSeparator) {
                    cells.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                else
                    sb.Append(c);
            }
            var tail = sb.ToString().Trim();
            if (tail.Length != 0)
                cells.Add(tail);
        }
        finally {
            sb.Release();
        }

        return cells;
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
            var boldMarkup = CreateStylized(Try(BoldToken), TextStyle.Bold).Debug("**");
            var italicMarkup = CreateStylized(ItalicToken, TextStyle.Italic).Debug("*");
            var spoilerMarkup = CreateStylized(Try(SpoilerToken), TextStyle.Spoiler).Debug("||");

            // One flat alternative list rather than Mention/Url/nonStylized nested inside it.
            // Pidgin's OneOf rents two Expected buffers from the array pool per invocation, and
            // this list is tried at every element, so each nesting level cost a rent/return pair.
            // The order is exactly what the nested form tried, and every alternative backtracks.
            Parser<char, Markup>[] inlineElements = [
                boldMarkup, italicMarkup, spoilerMarkup,
                NamedMention, UnnamedMention, preformattedText, WwwUrl, Email, Hashtag, NonWhitespaceText,
            ];

            // Text block for single-line content (list items) - no newlines allowed
            var textBlockSingleLine =
                SafeTryOneOf([.. inlineElements, StraySpecialText])
                    .AtLeastOnceSingleLineMarkup()
                    .Debug("<TextSingleLine>");

            // Text block (includes inline newlines for multi-line styled text in paragraphs)
            TextBlock =
                SafeTryOneOf([.. inlineElements, InlineNewLine])
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
                from special in CharRun.String(IsSpecialChar, 1)
                select TextMarkup.New(textMarkupKind, whitespace + special, true)
                ).Debug("<Unparsed>");

            var inlineParser =
                SafeTryOneOf(WhitespaceText, InlineNewLine, TextBlock, unparsedTextBlock).Debug("<InlineElement>")
                    .ManyMarkup().Debug("<Inline>");

            // Paragraph: collect all content until paragraph break or block element, then parse as inline
            // This allows styled text (**bold**) to span multiple lines
            var paragraph = (
                from firstLine in ParagraphLine
                from restParts in Try(
                    from nl in EndOfLine
                    from _ in Lookahead(InlineBreakAhead.SafeNot()) // not a paragraph break or a block start
                    from line in ParagraphLine
                    select "\n" + line // preserve newline in content
                ).Many()
                select BuildParagraph(firstLine, restParts, inlineParser)
                ).Debug("<Paragraph>");

            // Header: 1-3 '#' followed by whitespace and a single line of inline content
            var header = (
                from level in HeaderLevel
                from _ in CharRun.Skip(IsWhitespaceChar, 1)
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

            // Table: a header row, a delimiter row, and any number of body rows. Cells hold inline
            // markup only, and a body row is padded/truncated to the header's cell count (as in GFM).
            var table = (
                from headerLine in TableRowLine
                from _ in EndOfLine
                from delimiterLine in TableRowLine
                from bodyLines in Try(EndOfLine.Then(TableRowLine)).Many()
                select TryBuildTable(headerLine, delimiterLine, bodyLines, inlineParser))
                .Guard(x => x != null)
                .Select(x => x!)
                .Debug("<Table>");

            // Any standalone block (list/code/header/quote/table, or paragraph including empty/inline-only).
            var blockOrHeader = SafeTryOneOf(CodeBlock, listBlock, header, blockquote, table);
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
            var prev = first;
            foreach (var (leadingNewlines, item) in rest) {
                var prevIsNonEmptyPara = MarkupSeqFormatHelper.IsNonEmptyPara(prev);
                var currIsNonEmptyPara = MarkupSeqFormatHelper.IsNonEmptyPara(item);
                var currIsEmptyPara = MarkupSeqFormatHelper.IsEmptyPara(item);

                int minSep;
                if (currIsEmptyPara) {
                    // For a trailing/intervening empty paragraph the EmptyPara itself
                    // already accounts for one newline; pair it with the paragraph
                    // break when the predecessor is a non-empty paragraph, otherwise
                    // a single block boundary newline is enough.
                    minSep = prevIsNonEmptyPara ? 2 : 1;
                }
                else if (prevIsNonEmptyPara && currIsNonEmptyPara) {
                    // Adjacent non-empty paragraphs require a paragraph break.
                    minSep = 2;
                }
                else {
                    // Any other transition needs exactly one block boundary newline.
                    minSep = 1;
                }

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

        private static Markup BuildParagraph(
            string firstLine,
            IEnumerable<string> restParts,
            Parser<char, Markup> inlineParser)
        {
            // Concatenate all content (restParts already include newlines)
            var content = firstLine + string.Concat(restParts);
            return new ParagraphMarkup(inlineParser.ParseOrThrow(content, ParserConfiguration));
        }

        private static Markup BuildHeader(int level, string line, Parser<char, Markup> inlineParser)
            => new HeaderMarkup(level, inlineParser.ParseOrThrow(line, ParserConfiguration));

        private static Markup BuildBlockQuote(
            string firstLine,
            IEnumerable<string> restLines,
            Parser<char, Markup> inlineParser)
        {
            var content = firstLine + string.Concat(restLines);
            return new BlockQuoteMarkup(inlineParser.ParseOrThrow(content, ParserConfiguration));
        }

        private static Markup? TryBuildTable(
            string headerLine,
            string delimiterLine,
            IEnumerable<string> bodyLines,
            Parser<char, Markup> inlineParser)
        {
            if (TryParseTableAlignments(headerLine, delimiterLine) is not { } alignments)
                return null;

            var columnCount = alignments.Length;
            var header = BuildTableRow(headerLine, columnCount, inlineParser);
            var rows = bodyLines.Select(line => BuildTableRow(line, columnCount, inlineParser)).ToArray();
            return new TableMarkup(header, alignments, rows);
        }

        private static TableRowMarkup BuildTableRow(string line, int columnCount, Parser<char, Markup> inlineParser)
        {
            var cellTexts = SplitTableRow(line);
            var cells = new TableCellMarkup[columnCount];
            for (var i = 0; i < columnCount; i++) {
                var cellText = i < cellTexts.Count ? cellTexts[i] : "";
                cells[i] = new TableCellMarkup(inlineParser.ParseOrThrow(cellText, ParserConfiguration));
            }

            return new TableRowMarkup(cells);
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
