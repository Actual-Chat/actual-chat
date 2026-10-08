using Pidgin;

namespace ActualChat.Chat;

/// <summary>
/// Matches the target of a <c>[title](target)</c> link: a run without whitespace in which
/// parentheses are balanced, so the closing one is the first that has no opener.
/// The partial form is for a link that is still arriving: it may be empty or have an open parenthesis.
/// </summary>
internal sealed class LinkTargetParser(bool isPartial) : Parser<char, string>
{
    public static readonly LinkTargetParser Instance = new(false);
    public static readonly LinkTargetParser Partial = new(true);

    public override bool TryParse(
        ref ParseState<char> state,
        ref PooledList<Expected<char>> expecteds,
        [MaybeNullWhen(false)] out string result)
    {
        var span = state.LookAhead(CharRun.ChunkSize);
        var depth = 0;
        var length = 0;
        while (length < span.Length) {
            var c = span[length];
            if (char.IsWhiteSpace(c))
                break;
            if (c == '(')
                depth++;
            else if (c == ')') {
                if (depth == 0)
                    break;

                depth--;
            }
            length++;
        }
        if (!isPartial && (length == 0 || depth != 0)) {
            result = null;
            return false;
        }

        result = new string(span[..length]);
        state.Advance(length);
        return true;
    }
}
