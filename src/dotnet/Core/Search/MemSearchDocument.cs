using System.Text;

namespace ActualChat.Search;

/// <summary>
/// A search target reduced to a match blob: each word emitted as its camelCase / digit-boundary
/// suffixes, lowercased — e.g. "USA50" → " usa50_50". A space precedes a whole-word suffix, "_"
/// a mid-word one; a <see cref="MemSearchQuery"/> prefix-probes either.
/// </summary>
public readonly struct MemSearchDocument : IEquatable<MemSearchDocument>
{
    [ThreadStatic] private static StringBuilder? _builder;

    public string Value => field ?? "";
    public bool IsEmpty => Value.Length == 0;

    public MemSearchDocument(string? text)
    {
        var sb = _builder ??= new StringBuilder(256);
        try {
            AppendWords(sb, text);
            Value = sb.ToString();
        }
        finally {
            sb.Clear();
        }
    }

    public MemSearchDocument(params ReadOnlySpan<string?> fragments)
    {
        var sb = _builder ??= new StringBuilder(256);
        try {
            foreach (var fragment in fragments)
                AppendWords(sb, fragment);
            Value = sb.ToString();
        }
        finally {
            sb.Clear();
        }
    }

    public override string ToString()
        => Value;

    public bool IsMatch(MemSearchQuery query)
        => query.IsMatch(Value);

    public double GetCoverageScore(MemSearchQuery query)
    {
        // Higher is better: the query's summed best-suffix score over the document's word length.
        if (query.IsEmpty || Value.IsNullOrEmpty())
            return 0;

        var rawScore = query.GetRawScore(Value);
        if (rawScore <= 0)
            return 0;

        var totalChars = CountWordChars(Value);
        return totalChars == 0 ? 0 : rawScore / totalChars;
    }

    public MemSearchDocument OrNew(string? fallbackText)
        => IsEmpty ? new MemSearchDocument(fallbackText) : this;

    // Equality

    public bool Equals(MemSearchDocument other)
        => Value == other.Value;
    public override bool Equals(object? obj)
        => obj is MemSearchDocument other && Equals(other);
    public override int GetHashCode()
        => Value.GetHashCode();
    public static bool operator ==(MemSearchDocument left, MemSearchDocument right)
        => left.Equals(right);
    public static bool operator !=(MemSearchDocument left, MemSearchDocument right)
        => !left.Equals(right);

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

    internal readonly record struct Segment(int Start, int End, bool IsWholeWord);

    // Walks original text and yields, per word, the whole-word segment then each camelCase /
    // digit sub-segment. Every segment ends at the word's end; sub-segments are suffixes.
    internal ref struct SegmentEnumerator
    {
        private readonly ReadOnlySpan<char> _text;
        private int _scan;
        private int _wordEnd;
        private int _segmentStart;

        public Segment Current { get; private set; }

        public SegmentEnumerator(ReadOnlySpan<char> text)
        {
            _text = text;
            _scan = 0;
            _wordEnd = -1;
            _segmentStart = -1;
        }

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
