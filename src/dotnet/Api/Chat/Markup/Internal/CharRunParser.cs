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

/// <summary>
/// Matches a run that starts with a char satisfying <paramref name="isFirst"/> and continues with chars
/// satisfying <paramref name="predicate"/>, but consumes only the prefix of it that
/// <paramref name="getPrefixLength"/> accepts - the rest is left for the parsers that follow.
/// </summary>
internal sealed class CharRunPrefixParser(
    Func<char, bool> isFirst,
    Func<char, bool> predicate,
    CharRunPrefixLength getPrefixLength) : Parser<char, string>
{
    private Func<char, bool> IsFirst { get; } = isFirst;
    private Func<char, bool> Predicate { get; } = predicate;
    private CharRunPrefixLength GetPrefixLength { get; } = getPrefixLength;

    public override bool TryParse(
        ref ParseState<char> state,
        ref PooledList<Expected<char>> expecteds,
        [MaybeNullWhen(false)] out string result)
    {
        result = null;
        // The prefix is cut from the whole run, so the window grows until the run ends inside it
        var window = CharRun.ChunkSize;
        ReadOnlySpan<char> span;
        int count;
        while (true) {
            span = state.LookAhead(window);
            count = CharRun.CountMatching(span, Predicate);
            if (count < span.Length || span.Length < window || window > int.MaxValue / 2)
                break;

            window *= 2;
        }
        if (count < 2 || !IsFirst(span[0]))
            return false;

        var length = GetPrefixLength(span[..count]);
        if (length < 1)
            return false;

        result = new string(span[..length]);
        state.Advance(length);
        return true;
    }
}

internal delegate int CharRunPrefixLength(ReadOnlySpan<char> run);

internal static class CharRun
{
    // Large enough that every message this parser sees is one chunk; the loop exists so a
    // stream-backed parse can't silently truncate a run.
    public const int ChunkSize = 8192;

    public static Parser<char, string> String(Func<char, bool> predicate, int minCount = 0)
        => new CharRunParser(predicate, minCount);

    public static Parser<char, Unit> Skip(Func<char, bool> predicate, int minCount = 0)
        => new SkipCharRunParser(predicate, minCount);

    public static Parser<char, string> Prefix(
        Func<char, bool> isFirst,
        Func<char, bool> predicate,
        CharRunPrefixLength getPrefixLength)
        => new CharRunPrefixParser(isFirst, predicate, getPrefixLength);

    public static int CountMatching(ReadOnlySpan<char> span, Func<char, bool> predicate)
    {
        var count = 0;
        while (count < span.Length && predicate(span[count]))
            count++;
        return count;
    }
}
