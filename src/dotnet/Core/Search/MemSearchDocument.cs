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

        var wordStart = -1;
        for (var i = 0; i <= source.Length; i++) {
            var isWordChar = i < source.Length && char.IsLetterOrDigit(source[i]);
            if (isWordChar) {
                if (wordStart < 0)
                    wordStart = i;
            }
            else if (wordStart >= 0) {
                AppendWord(sb, source, wordStart, i);
                wordStart = -1;
            }
        }
    }

    private static void AppendWord(StringBuilder sb, string source, int wordStart, int wordEnd)
    {
        // Emit the whole word, then every camelCase / digit suffix — search is prefix-based, so
        // partial suffixes aren't needed. " " precedes the whole word, "_" a mid-word suffix.
        var segmentStart = wordStart;
        var prefix = ' ';
        while (true) {
            sb.Append(prefix);
            for (var j = segmentStart; j < wordEnd; j++)
                sb.Append(char.ToLower(source[j]));

            var nextStart = segmentStart + 1;
            while (nextStart < wordEnd && !IsSegmentBoundary(source, nextStart, wordEnd))
                nextStart++;
            if (nextStart >= wordEnd)
                return;

            segmentStart = nextStart;
            prefix = '_';
        }
    }

    private static bool IsSegmentBoundary(string source, int i, int wordEnd)
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
}
