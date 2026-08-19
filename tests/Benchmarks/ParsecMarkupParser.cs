using System.Text.RegularExpressions;
using ActualChat.Mathematics;
using ParsecSharp;
using ParsecSharp.Internal;
using static ParsecSharp.Parser;
using static ParsecSharp.Text;

namespace ActualChat.Chat;

// Measured against the Pidgin original: 2.4-2.8x slower and 30-59x more allocating, so this is
// not a candidate to replace it - see MarkupParserBenchmarks for the numbers. It stays as the
// record of that experiment and as a starting point if ParsecSharp ever grows a Many that
// doesn't allocate per token.

/// <summary>
/// A port of <see cref="MarkupParser"/> from Pidgin onto ParsecSharp, kept deliberately as a
/// drop-in: same options, same grammar, same <see cref="Markup"/> tree - proven over a corpus by
/// <c>MarkupParserEquivalence</c>. To adopt it, move this file to src/dotnet/Api/Chat/Markup/,
/// move the ParsecSharp PackageReference to Api.csproj, and rename the type to MarkupParser.
/// </summary>
#pragma warning disable CA1823 // Unused field ...
public sealed partial class ParsecMarkupParser : IMarkupParser
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

        return parser.Parse(text).CaseOf(_ => EmptyResult, success => success.Value);
    }

    // Character classes. The predicates are kept separate from the parsers because the char-run
    // parsers below are built from the predicate directly - see TakeWhileString.
    private static readonly Func<char, bool> IsWhitespaceChar =
        c => c is not ('\r' or '\n' or '\u2028') && char.IsWhiteSpace(c);
    private static readonly Func<char, bool> IsNotEndOfLineChar = c => c is not ('\r' or '\n' or '\u2028');
    private static readonly Func<char, bool> IsIdChar =
        c => char.IsLetterOrDigit(c) || c is '_' or '-' or ':' or '.' or '%' or '~';
    private static readonly Func<char, bool> IsSpecialChar = c => c is '*' or '`' or '@' or '|';
    private static readonly Func<char, bool> IsNotSpecialOrWhitespaceChar =
        c => !(char.IsWhiteSpace(c) || c is '*' or '`' or '@' or '|');
    private static readonly IParser<char, char> WhitespaceChar = Satisfy(IsWhitespaceChar);
    private static readonly IParser<char, char> IdChar = Satisfy(IsIdChar);
    private static readonly IParser<char, char> AnyWhitespaceChar = Satisfy(char.IsWhiteSpace);

    // Strings & line endings. EndOfLine matches Pidgin's: "\r\n" or "\n", never a lone "\r"
    // (ParseRaw normalizes the input, so only "\r\n" ever reaches it).
    private static readonly IParser<char, string> EndOfLine = String("\r\n").Or(String("\n"));
    private static readonly IParser<char, string> WhitespaceString = TakeWhileString(char.IsWhiteSpace);
    private static readonly IParser<char, string> NotEndOfLineString = TakeWhileString(IsNotEndOfLineChar);

    // Tokens

    private static readonly IParser<char, TextStyle> BoldToken = String("**").MapConst(TextStyle.Bold);
    private static readonly IParser<char, TextStyle> ItalicToken = Char('*').MapConst(TextStyle.Italic);
    private static readonly IParser<char, TextStyle> SpoilerToken = String("||").MapConst(TextStyle.Spoiler);
    private static readonly IParser<char, char> PreToken = Char('`');
    private static readonly IParser<char, char> NotPreToken = Satisfy(c => c != '`');
    private static readonly IParser<char, char> DoublePreToken = PreToken.Right(PreToken);
    private static readonly IParser<char, string> CodeBlockToken = String("```");
    private static readonly IParser<char, char> AtToken = Char('@');

    // ':' and '.' only join id segments — they're consumed as part of an id solely when another id
    // char follows. This keeps a trailing ':' or '.' out of the id, so "@`Name`a:c:1: text" (the
    // author-mention prefix in a multi-message copy) and "@`Name`u:id. Next" parse the mention and
    // leave the punctuation to the surrounding text.
    private static readonly IParser<char, char> IdBodyChar =
        Satisfy(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '%' or '~');
    private static readonly IParser<char, string> Id =
        IdBodyChar.Or(Satisfy(c => c is ':' or '.').Left(LookAhead(IdChar))).Many1().AsString();
    private static readonly IParser<char, string> QuotedName =
        PreToken.Right(NotPreToken.Or(DoublePreToken).Many().AsString()).Left(PreToken);

    // Regex: Url

    private static readonly UInt128 FirstUrlCharBits;
    private static readonly IParser<char, char> FirstUrlChar = Satisfy(c => FirstUrlCharBits.IsBitSet(c));
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
    private static readonly IParser<char, char> FirstEmailChar = Satisfy(c => FirstEmailCharBits.IsBitSet(c));
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
    private static readonly IParser<char, Markup> NonWhitespaceText =
        TakeWhileString(IsNotSpecialOrWhitespaceChar, 1).ToTextMarkup(TextMarkupKind.Plain, false);
    private static readonly IParser<char, Markup> WhitespaceText =
        TakeWhileString(IsWhitespaceChar, 1).ToTextMarkup(TextMarkupKind.Plain, false);

    // Header level: 1 to 3 '#' chars followed by whitespace (lookahead, not consumed)
    private static readonly IParser<char, int> HeaderLevel =
        TakeWhileString(c => c == '#', 1)
            .Where(s => s.Length is >= HeaderMarkup.MinLevel and <= HeaderMarkup.MaxLevel)
            .Map(s => s.Length)
            .Left(LookAhead(WhitespaceChar));

    // Table row: a line starting with '|'. That leading '|' is required, unlike in Markdown,
    // because '|' is also the spoiler token here - a pipe-less "a | b" line stays ordinary text.
    private static readonly IParser<char, string> TableRowLine =
        LookAhead(Char(TableMarkup.CellSeparator)).Right(NotEndOfLineString);
    // A table begins only where a row is followed by a delimiter row of matching cell count
    // ("| --- | :-: |"), so any other line starting with '|' (e.g. "||spoiler||") stays text.
    private static readonly IParser<char, char> TableStart = (
        from headerLine in TableRowLine
        from _ in EndOfLine
        from delimiterLine in TableRowLine
        select TryParseTableAlignments(headerLine, delimiterLine) != null)
        .Where(isTableStart => isTableStart)
        .MapConst(TableMarkup.CellSeparator);

    // Check what follows after a newline (used in lookahead)
    private static readonly IParser<char, char> ParagraphBreakAhead =
        EndOfLine.Right(EndOfLine).MapConst('\n'); // double newline = paragraph break
    private static readonly IParser<char, char> ListItemAhead =
        EndOfLine.Right(OneOf("-*").Left(WhitespaceChar));
    private static readonly IParser<char, string> CodeBlockAhead =
        EndOfLine.Right(CodeBlockToken);
    private static readonly IParser<char, int> HeaderAhead =
        EndOfLine.Right(HeaderLevel);
    private static readonly IParser<char, char> QuoteAhead =
        EndOfLine.Right(Char('>').Left(WhitespaceChar)); // "> " block-quote line start
    private static readonly IParser<char, char> TableAhead =
        EndOfLine.Right(TableStart);

    // Inline newline (single newline within inline content, not a paragraph break or a block start).
    // ParsecSharp's Not is already a non-consuming negative lookahead, so it needs no LookAhead.
    private static readonly IParser<char, Markup> InlineNewLine =
        Not(ParagraphBreakAhead) // not paragraph break
            .Right(Not(ListItemAhead)) // not list item start
            .Right(Not(CodeBlockAhead)) // not code block start
            .Right(Not(HeaderAhead)) // not header start
            .Right(Not(QuoteAhead)) // not block-quote start
            .Right(Not(TableAhead)) // not table start
            .Right(EndOfLine)
            .MapConst(NewLineMarkup.Instance as Markup);

    // Whitespace or newline (for inline content separators)
    private static readonly IParser<char, Markup> WhitespaceOrNewLine = Choice(InlineNewLine, WhitespaceText);

    // Mentions
    private static IParser<char, Markup> MentionParserFactory(string name = "") =>
        from id in Id
        let mentionId = MentionRef.TryParse(id, true)
        where mentionId != null
        select (Markup)MentionMarkup.New(mentionId, name);
    private static readonly IParser<char, Markup> NamedMention =
        // @`User Name`userId
        AtToken.Right(QuotedName).Bind(MentionParserFactory);
    private static readonly IParser<char, Markup> UnnamedMention =
        // @userId
        AtToken.Right(MentionParserFactory());
    private static readonly IParser<char, Markup> Mention = Choice(NamedMention, UnnamedMention);

    // Hashtag: '#' + a letter or '_', then letters, digits, '_', '-'. Only matches at an element
    // boundary — a mid-word '#' (c#5, item#2) stays inside the plain-text run because '#' is not
    // a special char for NotSpecialOrWhitespaceChar. All-digit tokens (#4121) are not hashtags,
    // and adjacent tags must be whitespace-separated — the trailing guard fails on '#a#b', which
    // then backtracks into a single plain-text run.
    private const int MaxHashtagLength = 64;
    private static readonly IParser<char, char> HashtagFirstChar = Satisfy(c => char.IsLetter(c) || c is '_');
    private static readonly Func<char, bool> IsHashtagChar = c => char.IsLetterOrDigit(c) || c is '_' or '-';
    private static readonly IParser<char, Markup> Hashtag = (
        from head in Char('#').Right(HashtagFirstChar)
        from tail in TakeWhileString(IsHashtagChar)
        select "#" + head + tail)
        .Where(s => s.Length <= 1 + MaxHashtagLength)
        .Left(Not(Char('#')))
        .Map(s => (Markup)new HashtagMarkup(s));

    // Preformatted text opener - the guard keeps a ``` code block fence out of an inline code span
    private static readonly IParser<char, ParsecSharp.Unit> PreformattedTextStart =
        Not(CodeBlockToken.Left(NotPreToken.OrEnd()));
    private static readonly IParser<char, string> PreformattedTextBody =
        NotPreToken.Or(DoublePreToken).Many().AsString();

    // Url
    private static IParser<char, Markup> WwwUrl => (
        from head in FirstUrlChar
        from tail in TakeWhileString(IsUrlChar, 1)
        select head + tail)
        .Where(s => UrlRegex.IsMatch(s))
        .Map(s => (Markup)new UrlMarkup(s, UrlMarkupKind.Www));
    private static IParser<char, Markup> Email => (
        from head in FirstEmailChar
        from tail in TakeWhileString(IsEmailChar, 1)
        select head + tail)
        .Where(s => EmailRegex.IsMatch(s))
        .Map(s => (Markup)new UrlMarkup(s, UrlMarkupKind.Email));
    private static readonly IParser<char, Markup> Url = Choice(WwwUrl, Email);

    // Fallback for single-line content: consume a run of stray '*'/'`'/'@' as plain text when no
    // styled/preformatted/mention/url markup matches. Without it, an unmatched '**' (e.g. the
    // **`a`/`b`** ambiguity) would stall list-item parsing and silently drop the rest of the message.
    private static readonly IParser<char, Markup> StraySpecialText =
        TakeWhileString(IsSpecialChar, 1).ToTextMarkup(TextMarkupKind.Plain, false);

    // Code block
    private static readonly IParser<char, string> CodeBlockWithLanguageStart =
        CodeBlockToken.Right(TakeWhileString(IsIdChar).Left(EndOfLine)); // Language
    private static readonly IParser<char, string> CodeBlockWithoutLanguageStart =
        CodeBlockToken.MapConst(""); // Language
    private static readonly IParser<char, char> CodeBlockEnd =
        SkipMany(WhitespaceChar).Right(CodeBlockToken).Right(LookAhead(AnyWhitespaceChar.OrEnd()));
    private static readonly IParser<char, string> CodeBlockLine =
        Not(CodeBlockEnd).Right(NotEndOfLineString);
    // End of an unclosed code block: matches end-of-input as a fallback when
    // the closing ``` was not provided. The resulting value is unused.
    private static readonly IParser<char, char> CodeBlockEndOrEof =
        CodeBlockEnd.Or(EndOfInput().MapConst(default(char)));
    private static readonly IParser<char, string> CodeBlockCode =
        CodeBlockLine
            .SeparatedOrEndBy(EndOfLine)
            .Map(lines => {
                var buffer = new List<string>();
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
                }
            });
    private static readonly IParser<char, Markup> CodeBlock =
        from language in Choice(CodeBlockWithLanguageStart, CodeBlockWithoutLanguageStart)
        from code in Optional(CodeBlockCode, "")
        from end in CodeBlockEndOrEof
        select (Markup)new CodeBlockMarkup(code, language.TrimEnd());

    // Block-level pieces that don't depend on any grammar variant, so they're built once rather
    // than per InternalParsers instance. Everything downstream of TextBlock or of the unparsed
    // text markup kind does vary, and lives in InternalParsers.Build instead.

    // A single paragraph line (can be empty - 0 or more chars)
    private static readonly IParser<char, string> ParagraphLine = NotEndOfLineString;

    // Check if next line starts a block element (CodeBlock, ListBlock, Header, BlockQuote, or Table)
    private static readonly IParser<char, char> BlockElementAhead =
        LookAhead(Choice(
            CodeBlockToken.MapConst('`'),
            OneOf("-*").Left(WhitespaceChar),
            HeaderLevel.MapConst('#'),
            Char('>').Left(WhitespaceChar),
            TableStart));

    private static readonly IParser<char, string> QuoteContentLine =
        Char('>').Right(WhitespaceChar).Right(ParagraphLine);

    private static readonly IParser<char, Markup> FullMarkup =
        new InternalParsers(false).Build();
    private static readonly IParser<char, Markup> FullWithUnparsedMarkup =
        new InternalParsers(true).Build();
    private static readonly IParser<char, Markup> IncompleteMarkup =
        new IncompleteInternalParsers(false).Build();
    private static readonly IParser<char, Markup> IncompleteWithUnparsedMarkup =
        new IncompleteInternalParsers(true).Build();

    // Type initializer
    static ParsecMarkupParser()
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

    private static TableColumnAlignment? TryParseTableAlignment(string cell)
    {
        // A delimiter cell is one or more '-' with an optional ':' on either side; ':' alone isn't one.
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

    private static IParser<char, string> TakeWhileString(Func<char, bool> predicate, int minCount = 0)
        => new TakeWhileStringParser(predicate, minCount);

    // Nested types

    /// <summary>
    /// The equivalent of Pidgin's <c>ManyString</c>. ParsecSharp's <c>Many</c> accumulates through
    /// <c>Enumerable.Append</c>, so every matched token allocates an iterator plus a wrapper - about
    /// 2 allocations per character for a char run. This walks the state directly instead.
    /// </summary>
    private sealed class TakeWhileStringParser(Func<char, bool> predicate, int minCount)
        : PrimitiveParser<char, string>
    {
        protected override IResult<char, string> Run<TState>(TState state)
        {
            var sb = ActualLab.Text.StringBuilderExt.Acquire();
            try {
                var next = state;
                while (next.HasValue && predicate(next.Current)) {
                    sb.Append(next.Current);
                    next = next.Next;
                }

                return sb.Length < minCount
                    ? ParsecSharp.Internal.Result.Failure<char, TState, string>(state)
                    : ParsecSharp.Internal.Result.Success<char, TState, string>(sb.ToString(), next);
            }
            finally {
                sb.Release();
            }
        }
    }

    private class InternalParsers(bool useUnparsedTextMarkup)
    {
        private bool UseUnparsedTextMarkup { get; } = useUnparsedTextMarkup;

        // Assigned by Build before any parsing, and read through Delay(...) so the stylized parsers
        // can recurse back into the text block that contains them.
        protected IParser<char, Markup> TextBlock { get; private set; } = null!;

        public IParser<char, Markup> Build()
        {
            var textMarkupKind = UseUnparsedTextMarkup ? TextMarkupKind.Unparsed : TextMarkupKind.Plain;

            var preformattedText = CreatePreformattedText();
            var nonStylizedMarkup = Choice(Mention, preformattedText, Url, Hashtag, NonWhitespaceText);
            var boldMarkup = CreateStylized(BoldToken, TextStyle.Bold);
            var italicMarkup = CreateStylized(ItalicToken, TextStyle.Italic);
            var spoilerMarkup = CreateStylized(SpoilerToken, TextStyle.Spoiler);

            // Text block for single-line content (list items) - no newlines allowed
            var textBlockSingleLine =
                Choice(boldMarkup, italicMarkup, spoilerMarkup, nonStylizedMarkup, StraySpecialText)
                    .AtLeastOnceMarkup(WhitespaceText);

            // Text block (includes inline newlines for multi-line styled text in paragraphs)
            TextBlock =
                Choice(boldMarkup, italicMarkup, spoilerMarkup, nonStylizedMarkup, InlineNewLine)
                    .AtLeastOnceMarkup(WhitespaceOrNewLine);

            // List block (list items use single-line text block - no newlines within items)
            var unorderedListItem =
                from _ in OneOf("-*").Left(WhitespaceChar)
                from content in textBlockSingleLine.ManyMarkup()
                select (Markup)new ListItemMarkup(content);
            var listBlock =
                from first in unorderedListItem
                from rest in EndOfLine.Right(unorderedListItem).Many()
                select (Markup)new ListMarkup(
                    new[] { first }.Concat(rest).Select(c => (ListItemMarkup)c).ToArray());
            // Explicitly typed: the query yields TextMarkup, and Choice below needs Markup
            IParser<char, Markup> unparsedTextBlock =
                from whitespace in WhitespaceString
                from special in TakeWhileString(IsSpecialChar, 1)
                select TextMarkup.New(textMarkupKind, whitespace + special, true);

            var inlineParser =
                Choice(InlineNewLine, WhitespaceText, TextBlock, unparsedTextBlock).ManyMarkup();

            // Paragraph: collect all content until paragraph break or block element, then parse as inline
            // This allows styled text (**bold**) to span multiple lines
            var paragraph =
                from firstLine in ParagraphLine
                from restParts in (
                    from nl in EndOfLine
                    from _ in Not(EndOfLine) // not empty line (paragraph break)
                    from __ in Not(BlockElementAhead) // not block element ahead
                    from line in ParagraphLine
                    select "\n" + line // preserve newline in content
                ).Many()
                select BuildParagraph(firstLine, restParts, inlineParser);

            // Header: 1-3 '#' followed by whitespace and a single line of inline content
            var header =
                from level in HeaderLevel
                from _ in WhitespaceChar.Many1()
                from line in ParagraphLine
                select BuildHeader(level, line.TrimEnd(), inlineParser);

            // Block quote: one or more consecutive "> " lines; inner content is inline markup
            // (mentions/styles/urls/emoji/newlines) — no nested block elements like code blocks.
            var blockquote =
                from firstLine in QuoteContentLine
                from restLines in (
                    from nl in EndOfLine
                    from line in QuoteContentLine
                    select "\n" + line
                ).Many()
                select BuildBlockQuote(firstLine, restLines, inlineParser);

            // Table: a header row, a delimiter row, and any number of body rows. Cells hold inline
            // markup only, and a body row is padded/truncated to the header's cell count (as in GFM).
            var table = (
                from headerLine in TableRowLine
                from _ in EndOfLine
                from delimiterLine in TableRowLine
                from bodyLines in EndOfLine.Right(TableRowLine).Many()
                select TryBuildTable(headerLine, delimiterLine, bodyLines, inlineParser))
                .Where(x => x != null)
                .Map(x => x!);

            // Any standalone block (list/code/header/quote/table, or paragraph including empty/inline-only).
            var blockOrHeader = Choice(CodeBlock, listBlock, header, blockquote, table);
            var block = Choice(blockOrHeader, paragraph);

            // After the first block, every subsequent block is preceded by one or more
            // newlines. We capture the count and let BuildBlockSequence translate
            // (leadingNewlines − minSep) extra newlines into ParagraphMarkup.Empty
            // entries — one per blank line beyond the minimum block-boundary separator.
            var separatorAndNextBlock =
                from nls in EndOfLine.Many1()
                from item in block
                select (nls.Count, item);

            return from first in block
                from rest in separatorAndNextBlock.Many()
                select BuildBlockSequence(first, rest);
        }

        // Protected methods

        protected virtual IParser<char, Markup> CreatePreformattedText()
            => PreformattedTextStart
                .Right(PreformattedTextBody.Between(PreToken))
                .Map(s => (Markup)new PreformattedTextMarkup(s));

        protected virtual IParser<char, Markup> CreateStylized(IParser<char, TextStyle> token, TextStyle style)
            => Delay(() => TextBlock).Between(token)
                .Map(t => (Markup)new StylizedMarkup(t, style));

        // Private methods

        private static Markup BuildBlockSequence(
            Markup first,
            IEnumerable<(int LeadingNewlines, Markup Item)> rest)
        {
            var items = new List<Markup> { first };
            var prev = first;
            foreach (var (leadingNewlines, item) in rest) {
                var prevIsNonEmptyPara = IsNonEmptyPara(prev);
                var currIsNonEmptyPara = IsNonEmptyPara(item);
                var currIsEmptyPara = IsEmptyPara(item);

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

        private static Markup BuildParagraph(
            string firstLine,
            IEnumerable<string> restParts,
            IParser<char, Markup> inlineParser)
        {
            // Concatenate all content (restParts already include newlines)
            var content = firstLine + string.Concat(restParts);
            return new ParagraphMarkup(ParseOrThrow(inlineParser, content));
        }

        private static Markup BuildHeader(int level, string line, IParser<char, Markup> inlineParser)
            => new HeaderMarkup(level, ParseOrThrow(inlineParser, line));

        private static Markup BuildBlockQuote(
            string firstLine,
            IEnumerable<string> restLines,
            IParser<char, Markup> inlineParser)
        {
            var content = firstLine + string.Concat(restLines);
            return new BlockQuoteMarkup(ParseOrThrow(inlineParser, content));
        }

        private static Markup? TryBuildTable(
            string headerLine,
            string delimiterLine,
            IEnumerable<string> bodyLines,
            IParser<char, Markup> inlineParser)
        {
            if (TryParseTableAlignments(headerLine, delimiterLine) is not { } alignments)
                return null;

            var columnCount = alignments.Length;
            var header = BuildTableRow(headerLine, columnCount, inlineParser);
            var rows = bodyLines.Select(line => BuildTableRow(line, columnCount, inlineParser)).ToArray();
            return new TableMarkup(header, alignments, rows);
        }

        private static TableRowMarkup BuildTableRow(string line, int columnCount, IParser<char, Markup> inlineParser)
        {
            var cellTexts = SplitTableRow(line);
            var cells = new TableCellMarkup[columnCount];
            for (var i = 0; i < columnCount; i++) {
                var cellText = i < cellTexts.Count ? cellTexts[i] : "";
                cells[i] = new TableCellMarkup(ParseOrThrow(inlineParser, cellText));
            }

            return new TableRowMarkup(cells);
        }

        // MarkupSeqFormatHelper is internal to the Api assembly, so these mirror it exactly:
        // an "empty paragraph" is one holding the shared PlainTextMarkup.Empty instance.
        private static bool IsEmptyPara(Markup m)
            => m is ParagraphMarkup p && p.Content == Markup.EmptyText;

        private static bool IsNonEmptyPara(Markup m)
            => m is ParagraphMarkup p && p.Content != Markup.EmptyText;

        private static Markup ParseOrThrow(IParser<char, Markup> parser, string text)
            => parser.Parse(text).CaseOf(
                failure => throw new FormatException(failure.Message),
                success => success.Value);
    }

    private sealed class IncompleteInternalParsers(bool useUnparsedTextMarkup)
        : InternalParsers(useUnparsedTextMarkup)
    {
        // Protected methods

        protected override IParser<char, Markup> CreatePreformattedText()
            => Choice(
                base.CreatePreformattedText(),
                PreformattedTextStart
                    .Right(PreToken)
                    .Right(PreformattedTextBody)
                    .Left(EndOfInput())
                    .Map(s => (Markup)new PreformattedTextMarkup(s) { IsIncomplete = true }));

        protected override IParser<char, Markup> CreateStylized(IParser<char, TextStyle> token, TextStyle style)
        {
            // A two-character closing token can be half-arrived as well ("**bold*", "||secret|").
            // Consuming that half keeps the span matched; without it the span degrades to literal
            // text, which for a spoiler means showing what it is meant to hide.
            var end = style switch {
                TextStyle.Bold => Optional(Char('*')).Right(EndOfInput()),
                TextStyle.Spoiler => Optional(Char('|')).Right(EndOfInput()),
                _ => EndOfInput(),
            };
            // The content must be non-empty: an empty match would let a nested span consume the
            // closing token of the span that encloses it, turning "**bold**" into an incomplete
            // bold wrapping an incomplete empty one.
            return Choice(
                base.CreateStylized(token, style),
                token
                    .Right(Delay(() => TextBlock))
                    .Left(end)
                    .Map(t => (Markup)new StylizedMarkup(t, style) { IsIncomplete = true }));
        }
    }
}

file static class ParsecParserExt
{
    // ParsecSharp declares Many/Many1 as plain statics; these wrappers keep the call sites
    // reading like the Pidgin original this file is ported from.
    public static IParser<char, IReadOnlyCollection<T>> Many<T>(this IParser<char, T> parser)
        => Parser.Many(parser);

    public static IParser<char, IReadOnlyCollection<T>> Many1<T>(this IParser<char, T> parser)
        => Parser.Many1(parser);

    public static IParser<char, Markup> ToTextMarkup(
        this IParser<char, string> parser,
        TextMarkupKind textMarkupKind,
        bool parseNewLines)
        => parser.Map(s => TextMarkup.New(textMarkupKind, s, parseNewLines));

    public static IParser<char, char> OrEnd(this IParser<char, char> parser)
        => Parser.EndOfInput<char>().MapConst(default(char)).Or(parser);

    public static IParser<char, Markup> JoinMarkup(this IParser<char, Markup> head, IParser<char, Markup> tail) =>
        from h in head
        from t in tail
        select h + t;

    public static IParser<char, Markup> ManyMarkup(this IParser<char, Markup> markup)
        => markup.Many().Map(Markup.Join);

    public static IParser<char, Markup> AtLeastOnceMarkup(
        this IParser<char, Markup> markup,
        IParser<char, Markup> separator)
        // A continuation element may be whitespace-separated OR adjacent (e.g. `a`/`b`); without
        // the adjacent case, styled spans like **`a`/`b`** stop after the first element.
        // `separator` is inline-newline-or-whitespace, or whitespace alone for single-line content.
        => markup
            .JoinMarkup(separator.JoinMarkup(markup).Or(markup).ManyMarkup())
            .JoinMarkup(separator.Or(Parser.Pure<char, Markup>(Markup.EmptyText)));
}
