using System.Diagnostics.CodeAnalysis;
using Pidgin;

namespace ActualChat.Chat;

// Pidgin's OneOf builds two PooledLists per invocation to pick the best error message, and this
// grammar runs it at every element. ParseRaw maps a failed parse to an empty result, so those
// messages are never read. Each alternative must backtrack on its own - ParserExt.SafeTryOneOf
// wraps every one in Try - because this parser doesn't reset the state between them.

/// <summary>
/// <c>Pidgin.Parser.OneOf</c> without the error bookkeeping: alternatives are tried in order and
/// their expecteds go straight to the caller's list.
/// </summary>
internal sealed class SafeOneOfParser<T>(Parser<char, T>[] parsers) : Parser<char, T>
{
    public override bool TryParse(
        ref ParseState<char> state,
        ref PooledList<Expected<char>> expecteds,
        [MaybeNullWhen(false)] out T result)
    {
        foreach (var parser in parsers) {
            if (parser.TryParse(ref state, ref expecteds, out result))
                return true;

            // Without this the list grows for the whole parse, since every failed alternative
            // anywhere in the grammar appends to it and nothing ever reads it.
            expecteds.Clear();
        }

        result = default;
        return false;
    }
}
