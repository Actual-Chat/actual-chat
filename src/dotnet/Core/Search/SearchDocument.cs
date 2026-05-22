using System.Text;

namespace ActualChat.Search;

/// <summary>
/// A search target reduced to a match blob: each word emitted as its camelCase / digit-boundary
/// suffixes, lowercased — e.g. "USA50" → " usa50_50". A space precedes a whole-word suffix, "_"
/// a mid-word one; a <see cref="SearchQuery"/> prefix-probes either.
/// </summary>
public readonly struct SearchDocument : IEquatable<SearchDocument>
{
    [ThreadStatic] private static StringBuilder? _builder;

    public string PreprocessedText => field ?? "";
    public bool IsEmpty => PreprocessedText.Length == 0;

    public static SearchDocument FromPreprocessedText(string preprocessedText)
        => new(preprocessedText, AssumeValid.Option);

    private SearchDocument(string preprocessedText, AssumeValid _)
        => PreprocessedText = preprocessedText;

    public SearchDocument(string? text)
    {
        var sb = _builder ??= new StringBuilder(256);
        try {
            AppendWords(sb, text);
            PreprocessedText = sb.ToString();
        }
        finally {
            sb.Clear();
        }
    }

    public SearchDocument(params ReadOnlySpan<string?> fragments)
    {
        var sb = _builder ??= new StringBuilder(256);
        try {
            foreach (var fragment in fragments)
                AppendWords(sb, fragment);
            PreprocessedText = sb.ToString();
        }
        finally {
            sb.Clear();
        }
    }

    public override string ToString()
        => PreprocessedText;

    public bool IsMatch(SearchQuery query)
        => query.IsMatch(PreprocessedText);

    public double GetCoverageScore(SearchQuery query)
    {
        // Higher is better: the query's summed best-suffix score over the document's word length.
        if (query.IsEmpty || PreprocessedText.IsNullOrEmpty())
            return 0;

        var rawScore = query.GetRawScore(PreprocessedText);
        if (rawScore <= 0)
            return 0;

        var totalChars = CountWordChars(PreprocessedText);
        return totalChars == 0 ? 0 : rawScore / totalChars;
    }

    public SearchDocument OrNew(string? fallbackText)
        => IsEmpty ? new SearchDocument(fallbackText) : this;

    // Equality

    public bool Equals(SearchDocument other) => PreprocessedText == other.PreprocessedText;
    public override bool Equals(object? obj) => obj is SearchDocument other && Equals(other);
    public override int GetHashCode() => PreprocessedText.GetHashCode();
    public static bool operator ==(SearchDocument left, SearchDocument right) => left.Equals(right);
    public static bool operator !=(SearchDocument left, SearchDocument right) => !left.Equals(right);

    // Private methods

    private static void AppendWords(StringBuilder sb, string? source)
    {
        if (source.IsNullOrEmpty())
            return;

        // " " precedes the whole-word segment, "_" each camelCase / digit suffix — search is
        // prefix-based, so partial suffixes aren't needed.
        foreach (var segment in new SegmentEnumerator(source)) {
            sb.Append(segment.IsWholeWord ? ' ' : '_');
            for (var j = segment.Start; j < segment.End; j++)
                sb.Append(char.ToLower(source[j]));
        }
    }

    private static bool IsSegmentBoundary(ReadOnlySpan<char> source, int i, int wordEnd)
    {
        // A segment starts at i on a letter/digit transition, a camelCase lower→upper transition,
        // or at the last uppercase of an acronym right before a lowercase letter (UIElement → UI|Element).
        var prev = source[i - 1];
        var cur = source[i];
        if (char.IsDigit(prev) != char.IsDigit(cur))
            return true;
        if (char.IsUpper(cur) && !char.IsUpper(prev))
            return true;

        return char.IsUpper(prev) && char.IsUpper(cur) && i + 1 < wordEnd && char.IsLower(source[i + 1]);
    }

    private static int CountWordChars(string value)
    {
        // Counts chars of whole-word (space-prefixed) suffixes only — the document's "real" length.
        var count = 0;
        var inWord = false;
        foreach (var c in value) {
            if (c == ' ')
                inWord = true;
            else if (c == '_')
                inWord = false;
            else if (inWord)
                count++;
        }
        return count;
    }

    // Nested types

    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Segment(int Start, int End, bool IsWholeWord);

    // Walks original text and yields, per word, the whole-word segment then each camelCase /
    // digit sub-segment. Every segment ends at the word's end; sub-segments are suffixes.
    [StructLayout(LayoutKind.Auto)]
    internal ref struct SegmentEnumerator(ReadOnlySpan<char> text)
    {
        private readonly ReadOnlySpan<char> _text = text;
        private int _scan = 0;
        private int _wordEnd = -1;
        private int _segmentStart = -1;

        public Segment Current { get; private set; }

        public readonly SegmentEnumerator GetEnumerator()
            => this;

        public bool MoveNext()
        {
            if (_wordEnd >= 0) {
                // Still inside a word — yield its next camelCase / digit sub-segment, if any.
                var next = _segmentStart + 1;
                while (next < _wordEnd && !IsSegmentBoundary(_text, next, _wordEnd))
                    next++;
                if (next < _wordEnd) {
                    _segmentStart = next;
                    Current = new Segment(next, _wordEnd, false);
                    return true;
                }
                _wordEnd = -1;
            }
            while (_scan < _text.Length && !char.IsLetterOrDigit(_text[_scan]))
                _scan++;
            if (_scan >= _text.Length)
                return false;

            var wordStart = _scan;
            while (_scan < _text.Length && char.IsLetterOrDigit(_text[_scan]))
                _scan++;
            _wordEnd = _scan;
            _segmentStart = wordStart;
            Current = new Segment(wordStart, _wordEnd, true);
            return true;
        }
    }
}
