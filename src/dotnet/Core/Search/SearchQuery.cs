using System.Buffers;

namespace ActualChat.Search;

/// <summary>
/// A query precompiled for fast matching: one <c>QueryWord</c> per query word, each carrying a
/// <see cref="SearchValues{T}"/> of its prefix needles plus per-suffix scoring data. Words are
/// ordered fewest-suffixes-first so <see cref="IsMatch"/> fails fast.
/// </summary>
public readonly struct SearchQuery : IEquatable<SearchQuery>
{
    public static SearchQuery Empty { get; } = new(null);

    private static readonly ArrayPool<Range<int>> RangePool = ArrayPool<Range<int>>.Shared;
    private static readonly ArrayPool<SearchMatchPart> MatchPartPool = ArrayPool<SearchMatchPart>.Shared;

    public string PreprocessedText => field ?? "";
    public bool MatchSuffixes { get; }
    public Word[] Words => field ?? Empty.Words;
    public bool IsEmpty => PreprocessedText.Length == 0;

    public SearchQuery(string? text, bool matchSuffixes = true)
    {
        PreprocessedText = new SearchDocument(text).PreprocessedText;
        MatchSuffixes = matchSuffixes;
        Words = BuildWords(PreprocessedText);
    }

    public override string ToString()
        => Words.ToDelimitedString(", ");

    public SearchMatchPart[] GetMatchParts(string text)
    {
        // Highlight ranges over the original text: for each query word, the longest suffix that
        // prefix-matches a text segment. In exact mode only a whole-segment match counts.
        if (IsEmpty || text.IsNullOrEmpty())
            return [];

        var ranges = new RefArrayPoolBuffer<Range<int>>(RangePool, Words.Length);
        try {
            foreach (var word in Words) {
                foreach (var segment in new SearchDocument.SegmentEnumerator(text)) {
                    var length = MatchLength(text, segment, word);
                    if (length > 0)
                        ranges.Add(new Range<int>(segment.Start, segment.Start + length));
                }
            }
            if (ranges.Count == 0)
                return [];

            ranges.Array.AsSpan(0, ranges.Count).Sort(static (a, b) => a.Start != b.Start
                ? a.Start.CompareTo(b.Start)
                : a.End.CompareTo(b.End));
            return Merge(ranges.WrittenSpan);
        }
        finally {
            ranges.Release();
        }
        static SearchMatchPart[] Merge(ReadOnlySpan<Range<int>> sortedRanges) {
            // Ranges arrive sorted; collapse overlapping / adjacent ones into disjoint ascending parts.
            var parts = new RefArrayPoolBuffer<SearchMatchPart>(MatchPartPool, sortedRanges.Length);
            try {
                var current = sortedRanges[0];
                for (var i = 1; i < sortedRanges.Length; i++) {
                    var next = sortedRanges[i];
                    if (next.Start <= current.End)
                        current = new Range<int>(current.Start, Math.Max(current.End, next.End));
                    else {
                        parts.Add(new SearchMatchPart(current, 1));
                        current = next;
                    }
                }
                parts.Add(new SearchMatchPart(current, 1));
                return parts.ToArray();
            }
            finally {
                parts.Release();
            }
        }
    }

    // Equality

    public bool Equals(SearchQuery other) => PreprocessedText == other.PreprocessedText;
    public override bool Equals(object? obj) => obj is SearchQuery other && Equals(other);
    public override int GetHashCode() => PreprocessedText.GetHashCode();
    public static bool operator ==(SearchQuery left, SearchQuery right) => left.Equals(right);
    public static bool operator !=(SearchQuery left, SearchQuery right) => !left.Equals(right);

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

    private static Word[] BuildWords(string preprocessedText)
    {
        if (preprocessedText.Length == 0)
            return [];

        var words = new List<Word>();
        var start = 0;
        for (var i = 1; i <= preprocessedText.Length; i++) {
            if (i == preprocessedText.Length || preprocessedText[i] == ' ') {
                words.Add(Word.From(preprocessedText[start..i]));
                start = i;
            }
        }
        // Fewest suffixes first: a word with fewer alternatives is likelier to miss — fail fast.
        words.Sort(static (a, b) => a.Suffixes.Length.CompareTo(b.Suffixes.Length));
        return words.ToArray();
    }

    private int MatchLength(string text, SearchDocument.Segment segment, Word word)
    {
        // Suffixes run longest-first; the first prefix hit is the longest match for this segment.
        var segmentLength = segment.End - segment.Start;
        foreach (var suffix in word.Suffixes) {
            var suffixText = suffix.ExpectedNeedle.AsSpan(1);
            var fits = MatchSuffixes
                ? suffixText.Length <= segmentLength
                : suffixText.Length == segmentLength;
            if (fits && IsPrefix(text, segment.Start, suffixText))
                return suffixText.Length;
            if (!MatchSuffixes)
                return 0;
        }
        return 0;
    }

    private static bool IsPrefix(string text, int start, ReadOnlySpan<char> suffix)
    {
        for (var i = 0; i < suffix.Length; i++)
            if (char.ToLower(text[start + i]) != suffix[i])
                return false;
        return true;
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
