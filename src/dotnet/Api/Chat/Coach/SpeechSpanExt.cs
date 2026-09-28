namespace ActualChat.Chat;

public static class SpeechSpanExt
{
    // One slot per markup word holding the span that touches it (its kind, word and synonyms), null
    // where none does; null overall when nothing maps. The words carry trailing whitespace, so a span
    // start inside a word's TextRange is that word, and a phrase ("you know") covers every word its
    // range reaches into.
    public static SpeechSpan?[]? MapToWords(this IReadOnlyList<SpeechSpan> spans, PlayableTextMarkup markup)
    {
        if (spans.Count == 0)
            return null;

        var words = markup.Words;
        if (words.Length == 0)
            return null;

        SpeechSpan?[]? result = null;
        foreach (var span in spans) {
            var index = FindWord(words, span.Start);
            if (index < 0)
                continue;

            result ??= new SpeechSpan?[words.Length];
            var end = span.Start + Math.Max(span.Length, 1);
            for (var i = index; i < words.Length && words[i].TextRange.Start < end; i++)
                result[i] ??= span;
        }
        return result;
    }

    private static int FindWord(PlayableTextMarkup.Word[] words, int position)
    {
        if (position < 0)
            return -1;

        var low = 0;
        var high = words.Length - 1;
        while (low <= high) {
            var mid = (low + high) >> 1;
            var range = words[mid].TextRange;
            if (position < range.Start)
                high = mid - 1;
            else if (position >= range.End)
                low = mid + 1;
            else
                return mid;
        }
        return -1;
    }
}
