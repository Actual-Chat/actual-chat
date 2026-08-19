using System.Text;
using Pidgin;
using Unit = Pidgin.Unit;

namespace ActualChat.Chat;

// AtLeastOnceString/ManyString/SkipMany are built on Pidgin's chain combinator, which walks one
// parser invocation per character. These read the upcoming span directly instead: one scan, and
// for the string form one allocation. The chain parser was the largest remaining self-time item
// in a profile of this grammar, and character runs are what it was mostly doing.

/// <summary>
/// Matches a run of characters satisfying <paramref name="predicate"/>, as a string.
/// </summary>
internal sealed class CharRunParser(Func<char, bool> predicate, int minCount) : Parser<char, string>
{
    public override bool TryParse(
        ref ParseState<char> state,
        ref PooledList<Expected<char>> expecteds,
        [MaybeNullWhen(false)] out string result)
    {
        var builder = (StringBuilder?)null;
        var total = 0;
        while (true) {
            var span = state.LookAhead(CharRun.ChunkSize);
            var count = CharRun.CountMatching(span, predicate);
            total += count;
            if (count < span.Length || span.Length < CharRun.ChunkSize) {
                // The run ends inside this chunk, so it's the common single-allocation case
                if (total < minCount) {
                    result = null;
                    return false;
                }

                result = builder == null
                    ? new string(span[..count])
                    : builder.Append(span[..count]).ToString();
                state.Advance(count);
                return true;
            }

            // The run filled the chunk: keep the text and look further
            builder ??= new StringBuilder();
            builder.Append(span);
            state.Advance(count);
        }
    }
}

/// <summary>
/// Matches a run of characters satisfying <paramref name="predicate"/> without materializing it.
/// </summary>
internal sealed class SkipCharRunParser(Func<char, bool> predicate, int minCount) : Parser<char, Unit>
{
    public override bool TryParse(
        ref ParseState<char> state,
        ref PooledList<Expected<char>> expecteds,
        [MaybeNullWhen(false)] out Unit result)
    {
        result = Unit.Value;
        var total = 0;
        while (true) {
            var span = state.LookAhead(CharRun.ChunkSize);
            var count = CharRun.CountMatching(span, predicate);
            total += count;
            if (count < span.Length || span.Length < CharRun.ChunkSize) {
                if (total < minCount)
                    return false;

                state.Advance(count);
                return true;
            }

            state.Advance(count);
        }
    }
}

internal static class CharRun
{
    // Large enough that every message this parser sees is one chunk; the loop exists so a
    // stream-backed parse can't silently truncate a run.
    public const int ChunkSize = 8192;

    public static Parser<char, string> String(Func<char, bool> predicate, int minCount = 0)
        => new CharRunParser(predicate, minCount);

    public static Parser<char, Unit> Skip(Func<char, bool> predicate, int minCount = 0)
        => new SkipCharRunParser(predicate, minCount);

    public static int CountMatching(ReadOnlySpan<char> span, Func<char, bool> predicate)
    {
        var count = 0;
        while (count < span.Length && predicate(span[count]))
            count++;
        return count;
    }
}
