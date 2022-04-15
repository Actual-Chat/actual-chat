using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;

namespace Markdig.Extensions.Mentions;

public class MentionParser : InlineParser
{
    public MentionOptions Options { get; }

    public MentionParser(MentionOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        OpeningCharacters = new[] { '<' };
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var match = slice.CurrentChar;
        // Previous char must be a whitespace
        var previousChar = slice.PeekCharExtra(-1);
        if (!previousChar.IsWhiteSpaceOrZero())
            return false;

        var startPosition = slice.Start;
        if (slice.NextChar() != '@')
            return false;

        bool isMatching = false;
        var builder = StringBuilderCache.Local();
        var c = slice.NextChar();
        while (!c.IsZero()) {
            var isCompletionChar = c == '>';
            if (isCompletionChar) {
                slice.SkipChar();
                isMatching = builder.Length > 0;
                break;
            }
            var isSpecialChar = c == '-' || c == ':';
            var isValidChar = isSpecialChar || c.IsAlphaNumeric();
            if (!isValidChar)
                break;
            builder.Append(c);
            c = slice.NextChar();
        }

        if (isMatching) {
            var mentionId = builder.ToString();
            var isValidMentionId = Options.ValidateMentionId(mentionId);
            if (!isValidMentionId)
                return false;
            var spanStart = processor.GetSourcePosition(startPosition, out int line, out int column);
            var spanEnd = processor.GetSourcePosition(slice.Start - 1);
            var inline = new MentionInline(mentionId) {
                Span = new SourceSpan(spanStart, spanEnd),
                Line = line,
                Column = column,
            };
            processor.Inline = inline;
            return true;
        }
        return false;
    }
}
