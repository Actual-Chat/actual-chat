namespace ActualChat.Chat.ML;

public sealed record SpeechChunk(int Start, int Length, string Text, string Context);

/// <summary>
/// Cuts a transcript into pieces the tagger can take in parallel: whole sentences merged up to a word
/// target, a sentence longer than the cap cut at word boundaries. Every chunk knows the sentence that
/// precedes it, so the tagger can judge an ambiguous word at the start of the chunk.
/// </summary>
public static class SpeechChunker
{
    // A script without word spaces has no words to count: a "word" is this many characters
    private const int CharsPerUnit = 3;
    private const string Terminators = ".!?…。！？";
    private const string Closers = "\"'”’»)]";

    public static IReadOnlyList<SpeechChunk> Split(
        string text, bool isWordSplittable, int minWords, int maxWords, int contextWords, int maxChunks)
    {
        if (text.IsNullOrEmpty())
            return [];

        var sentences = FindSentences(text, isWordSplittable);
        var total = sentences.Sum(s => Units(text, s.Start, s.End, isWordSplittable));
        var target = Math.Max(minWords, (int)Math.Ceiling(total / (double)Math.Max(1, maxChunks)));
        var cap = Math.Max(maxWords, target + target / 2);
        if (total <= cap)
            return [new SpeechChunk(0, text.Length, text, "")];

        var pieces = sentences.SelectMany(s => CutLongSentence(text, s, cap, isWordSplittable)).ToList();
        var groups = new List<(int First, int Last, int Units)>();
        var first = 0;
        var units = 0;
        for (var i = 0; i < pieces.Count; i++) {
            units += pieces[i].Units;
            if (units >= target && i < pieces.Count - 1) {
                groups.Add((first, i, units));
                first = i + 1;
                units = 0;
            }
        }
        if (first < pieces.Count) {
            if (groups.Count > 0 && units < target / 2 && groups[^1].Units + units <= cap * 3 / 2)
                groups[^1] = (groups[^1].First, pieces.Count - 1, groups[^1].Units + units);
            else
                groups.Add((first, pieces.Count - 1, units));
        }

        var chunks = new List<SpeechChunk>();
        foreach (var group in groups) {
            var start = pieces[group.First].Start;
            var end = pieces[group.Last].End;
            var context = group.First == 0
                ? ""
                : Context(text, pieces[group.First - 1].SentenceStart, start, contextWords, isWordSplittable);
            chunks.Add(new SpeechChunk(start, end - start, text[start..end], context));
        }
        return chunks;
    }

    internal static IReadOnlyList<int> SentenceEnds(string text, bool isWordSplittable)
        // Where each sentence of the text ends, the whitespace after it included
        => FindSentences(text, isWordSplittable).Select(s => s.End).ToList();

    internal static int CountUnits(string text, int start, int end, bool isWordSplittable)
        => Units(text, start, end, isWordSplittable);

    internal static string SentenceBefore(string text, int position, int contextWords, bool isWordSplittable)
    {
        // The sentence that ends at the position, cut to its last words
        var sentence = FindSentences(text, isWordSplittable).LastOrDefault(s => s.End <= position);
        return sentence is null ? "" : Context(text, sentence.Start, sentence.End, contextWords, isWordSplittable);
    }

    // Private methods

    private static List<Piece> FindSentences(string text, bool isWordSplittable)
    {
        var sentences = new List<Piece>();
        var start = 0;
        var i = 0;
        while (i < text.Length) {
            var c = text[i];
            if (c == '\n' || Terminators.Contains(c)) {
                var end = i + 1;
                while (end < text.Length && (Terminators.Contains(text[end]) || Closers.Contains(text[end])))
                    end++;
                var isBoundary = end >= text.Length || char.IsWhiteSpace(text[end]) || !isWordSplittable;
                if (isBoundary) {
                    while (end < text.Length && char.IsWhiteSpace(text[end]))
                        end++;
                    sentences.Add(new Piece(start, end, start, 0));
                    start = end;
                    i = end;
                    continue;
                }
                i = end;
                continue;
            }
            i++;
        }
        if (start < text.Length)
            sentences.Add(new Piece(start, text.Length, start, 0));
        return sentences
            .Select(s => s with { Units = Units(text, s.Start, s.End, isWordSplittable) })
            .ToList();
    }

    private static IEnumerable<Piece> CutLongSentence(string text, Piece sentence, int cap, bool isWordSplittable)
    {
        if (sentence.Units <= cap) {
            yield return sentence;
            yield break;
        }

        var start = sentence.Start;
        while (start < sentence.End) {
            var end = FindCutEnd(text, start, sentence.End, cap, isWordSplittable);
            yield return new Piece(start, end, sentence.Start, Units(text, start, end, isWordSplittable));
            start = end;
        }
    }

    private static int FindCutEnd(string text, int start, int limit, int cap, bool isWordSplittable)
    {
        if (!isWordSplittable)
            return Math.Min(limit, start + cap * CharsPerUnit);

        var i = start;
        var words = 0;
        while (i < limit) {
            while (i < limit && char.IsWhiteSpace(text[i]))
                i++;
            if (i >= limit)
                break;
            if (words == cap)
                return i;

            while (i < limit && !char.IsWhiteSpace(text[i]))
                i++;
            words++;
        }
        return limit;
    }

    private static int Units(string text, int start, int end, bool isWordSplittable)
    {
        if (!isWordSplittable)
            return (end - start + CharsPerUnit - 1) / CharsPerUnit;

        var count = 0;
        var inWord = false;
        for (var i = start; i < end; i++) {
            var isSpace = char.IsWhiteSpace(text[i]);
            if (!isSpace && !inWord)
                count++;
            inWord = !isSpace;
        }
        return count;
    }

    private static string Context(string text, int from, int to, int contextWords, bool isWordSplittable)
    {
        var context = text[from..to].Trim();
        if (!isWordSplittable) {
            var maxChars = contextWords * CharsPerUnit;
            return context.Length <= maxChars ? context : context[^maxChars..];
        }

        var words = context.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= contextWords ? context : string.Join(" ", words[^contextWords..]);
    }

    // Nested types

    private sealed record Piece(int Start, int End, int SentenceStart, int Units);
}
