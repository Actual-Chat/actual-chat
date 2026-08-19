using System.Diagnostics.CodeAnalysis;
using Pidgin;
using Unit = Pidgin.Unit;

namespace ActualChat.Chat;

// Same contract as Pidgin's Not, including that neither outcome rewinds - every use here sits
// inside a Lookahead, which does. What it drops is the PooledList Pidgin builds per call to
// collect the child's expecteds before discarding them.

/// <summary>
/// Succeeds exactly when the wrapped parser fails, without producing an error message.
/// </summary>
internal sealed class SafeNotParser<T>(Parser<char, T> parser) : Parser<char, Unit>
{
    public override bool TryParse(
        ref ParseState<char> state,
        ref PooledList<Expected<char>> expecteds,
        [MaybeNullWhen(false)] out Unit result)
    {
        result = Unit.Value;
        var success = parser.TryParse(ref state, ref expecteds, out _);
        expecteds.Clear(); // Pidgin discards the child's expecteds here, and nothing reads them
        return !success;
    }
}
