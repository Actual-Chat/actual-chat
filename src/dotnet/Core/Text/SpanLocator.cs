using ActualChat.Mathematics;

namespace ActualChat;

/// <summary>
/// Locates the n-th whole-word occurrence of a word or phrase in a text; used to validate
/// LLM-returned spans instead of trusting their offsets.
/// </summary>
public static class SpanLocator
{
    // isWholeWord = false is for scripts without word spaces, where a match inside a run of letters
    // is the only kind there is.
    public static Range<int>? Locate(string text, string word, int occurrence, bool isWholeWord = true)
    {
        if (occurrence < 1)
            return null;

        foreach (var range in LocateAll(text, word, isWholeWord))
            if (--occurrence == 0)
                return range;
        return null;
    }

    public static IEnumerable<Range<int>> LocateAll(string text, string word, bool isWholeWord = true)
    {
        // Every occurrence in text order, found in one pass
        if (text.IsNullOrEmpty() || word.IsNullOrWhiteSpace())
            yield break;

        if (!isWholeWord) {
            var substring = word.Trim();
            var at = 0;
            while ((at = text.IndexOf(substring, at, StringComparison.OrdinalIgnoreCase)) >= 0) {
                yield return new Range<int>(at, at + substring.Length);

                at += substring.Length;
            }
            yield break;
        }

        var parts = word.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var i = 0;
        while (i < text.Length) {
            var start = SkipNonWord(text, i);
            if (start >= text.Length)
                yield break;

            var end = MatchPhrase(text, start, parts);
            if (end > 0) {
                yield return new Range<int>(start, end);

                i = end;
                continue;
            }
            i = SkipWord(text, start);
        }
    }

    // Private methods

    private static int MatchPhrase(string text, int start, string[] parts)
    {
        var pos = start;
        for (var p = 0; p < parts.Length; p++) {
            if (p > 0) {
                var next = SkipNonWord(text, pos);
                if (next == pos)
                    return 0;

                pos = next;
            }
            var wordEnd = SkipWord(text, pos);
            if (wordEnd - pos != parts[p].Length)
                return 0;
            if (!text.AsSpan(pos, parts[p].Length).Equals(parts[p], StringComparison.OrdinalIgnoreCase))
                return 0;

            pos = wordEnd;
        }
        return pos;
    }

    private static int SkipNonWord(string text, int i)
    {
        while (i < text.Length && !IsWordChar(text, i))
            i++;
        return i;
    }

    private static int SkipWord(string text, int i)
    {
        while (i < text.Length && IsWordChar(text, i))
            i++;
        return i;
    }

    private static bool IsWordChar(string text, int i)
    {
        // An apostrophe or hyphen counts as part of a word only between two letters ("don't", "э-э"),
        // so "(um)" and "- word" still split where a reader expects.
        var c = text[i];
        if (char.IsLetterOrDigit(c))
            return true;
        if (c is not ('\'' or '-' or '’'))
            return false;

        return i > 0 && i + 1 < text.Length && char.IsLetterOrDigit(text[i - 1]) && char.IsLetterOrDigit(text[i + 1]);
    }
}
