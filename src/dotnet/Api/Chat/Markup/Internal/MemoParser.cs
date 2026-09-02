using System.Diagnostics.CodeAnalysis;
using Pidgin;

namespace ActualChat.Chat;

// The wrapped parser must fail without consuming input, which every text block does: its first
// element is a Try'd alternative, and once that matched the rest of it can't fail. A replayed
// failure consumes nothing, so a parser that could fail after consuming would replay differently.

/// <summary>
/// Caches the wrapped parser's outcome per input position for the length of one parse scope,
/// so a suffix parsed once under one enclosing span isn't parsed again under another.
/// </summary>
internal sealed class MemoParser(Parser<char, Markup> parser) : Parser<char, Markup>
{
    private readonly int _index = ParseMemo.NextParserIndex();

    public override bool TryParse(
        ref ParseState<char> state,
        ref PooledList<Expected<char>> expecteds,
        [MaybeNullWhen(false)] out Markup result)
    {
        var memo = ParseMemo.TryGetTable(_index);
        if (memo == null)
            return parser.TryParse(ref state, ref expecteds, out result);

        var start = state.Location;
        if (memo.TryGetValue(start, out var entry)) {
            result = entry.Result;
            if (result == null)
                return false;

            state.LookAhead(entry.Length);
            state.Advance(entry.Length);
            return true;
        }

        var isParsed = parser.TryParse(ref state, ref expecteds, out result);
        memo[start] = isParsed ? (result, checked((int)(state.Location - start))) : (null, 0);
        return isParsed;
    }
}
