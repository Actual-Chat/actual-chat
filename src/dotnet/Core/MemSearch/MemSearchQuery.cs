using System.Buffers;

namespace ActualChat;

/// <summary>
/// A query precompiled for fast matching: one <c>QueryWord</c> per query word, each carrying a
/// <see cref="SearchValues{T}"/> of its prefix needles plus per-suffix scoring data. Words are
/// ordered fewest-suffixes-first so <see cref="IsMatch"/> fails fast.
/// </summary>
public readonly struct MemSearchQuery(string? source)
{
    public static MemSearchQuery Empty { get; } = new(null);

    public Word[] Words { get => field ?? Empty.Words; } = BuildWords(source);
    public bool IsEmpty => Words.Length == 0;

    public override string ToString()
        => Words.ToDelimitedString(", ");

    // Internal methods

    internal bool IsMatch(string documentValue)
    {
        foreach (var word in Words)
            if (!word.Matches(documentValue))
                return false;
        return true;
    }

    internal double GetRawScore(string documentValue)
    {
        var sum = 0d;
        foreach (var word in Words) {
            var wordScore = word.Score(documentValue);
            if (wordScore <= 0)
                return 0;

            sum += wordScore;
        }
        return sum;
    }

    // Private methods

    private static Word[] BuildWords(string? source)
    {
        // The query is tokenized like a document, split into one blob per word, and each blob
        // is precompiled into a QueryWord.
        var value = new MemSearchDocument(source).Value;
        if (value.Length == 0)
            return [];

        var words = new List<Word>();
        var start = 0;
        for (var i = 1; i <= value.Length; i++) {
            if (i == value.Length || value[i] == ' ') {
                words.Add(Word.From(value[start..i]));
                start = i;
            }
        }
        // Fewest suffixes first: a word with fewer alternatives is likelier to miss — fail fast.
        words.Sort(static (a, b) => a.Suffixes.Length.CompareTo(b.Suffixes.Length));
        return words.ToArray();
    }

    // Nested types

    public readonly struct Word
    {
        private const double ExpectedPrefixBonus = 0.5;

        public readonly string Value;
        public readonly SearchValues<string> Needles;
        public readonly QuerySuffix[] Suffixes; // longest first

        public static Word From(string value)
        {
            // wordBlob = " mcdon5_don5_5" — suffixes longest-first; each yields a needle with its
            // expected prefix (' ' for the whole word, '_' for a mid-word suffix) and the flipped one.
            var suffixes = new List<QuerySuffix>();
            var needles = new List<string>();
            var i = 0;
            while (i < value.Length) {
                var expectedPrefix = value[i];
                var suffixStart = i + 1;
                var suffixEnd = suffixStart;
                while (suffixEnd < value.Length && value[suffixEnd] is not (' ' or '_'))
                    suffixEnd++;

                var suffix = value.Substring(suffixStart, suffixEnd - suffixStart);
                var expectedNeedle = expectedPrefix + suffix;
                var otherNeedle = (expectedPrefix == ' ' ? '_' : ' ') + suffix;
                suffixes.Add(new QuerySuffix(suffix.Length, expectedNeedle, otherNeedle));
                needles.Add(expectedNeedle);
                needles.Add(otherNeedle);
                i = suffixEnd;
            }
            var needleValues = SearchValues.Create(needles.ToArray(), StringComparison.Ordinal);
            return new Word(value, needleValues, suffixes.ToArray());
        }

        private Word(string value, SearchValues<string> needles, QuerySuffix[] suffixes)
        {
            Value = value;
            Needles = needles;
            Suffixes = suffixes;
        }

        public override string ToString()
            => $"`{Value}`[{Suffixes.Length}]";

        public bool Matches(string documentValue)
            => documentValue.AsSpan().IndexOfAny(Needles) >= 0;

        public double Score(string documentValue)
        {
            // Suffixes run longest-first, so the first hit is the largest match.
            foreach (var suffix in Suffixes) {
                if (documentValue.Contains(suffix.ExpectedNeedle))
                    return suffix.Length + ExpectedPrefixBonus;
                if (documentValue.Contains(suffix.OtherNeedle))
                    return suffix.Length;
            }
            return 0;
        }
    }

    public readonly record struct QuerySuffix(
        int Length,
        string ExpectedNeedle,
        string OtherNeedle);
}
