using System.Diagnostics.CodeAnalysis;
using Pidgin;

namespace ActualChat.Chat;

// Same contract as Pidgin's Assert (which is what .Where builds), including that a rejected
// result doesn't rewind - every use here sits inside a Try, which does. What it drops is the
// PooledList Pidgin builds per call, plus the error message it formats for a rejected result.

/// <summary>
/// Runs the wrapped parser and fails when its result doesn't satisfy the predicate.
/// </summary>
internal sealed class GuardParser<T>(Parser<char, T> parser, Func<T, bool> predicate) : Parser<char, T>
{
    public override bool TryParse(
        ref ParseState<char> state,
        ref PooledList<Expected<char>> expecteds,
        [MaybeNullWhen(false)] out T result)
    {
        if (!parser.TryParse(ref state, ref expecteds, out result))
            return false;
        if (predicate(result))
            return true;

        result = default;
        return false;
    }
}
