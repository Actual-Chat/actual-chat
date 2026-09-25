namespace ActualChat.Chat;

/// <summary>
/// Word-level counts of one transcript, computed over <see cref="PlayableTextMarkup.Words"/>
/// so offsets agree with the client's word indices.
/// </summary>
public sealed record SpeechTextStats(
    int Words,
    int Sentences,
    int Questions,
    int Repetitions,
    int DistinctWords,
    ApiArray<SpeechSpan> RepetitionSpans)
{
    private static readonly HashSet<string> NoWordSpaceIsoCodes = ["ja", "zh", "th", "km", "lo", "my"];

    public static bool IsWordSplittable(Language? language)
        => language is null || !NoWordSpaceIsoCodes.Contains(language.IsoCode);

    public static SpeechTextStats? Compute(PlayableTextMarkup markup)
    {
        var words = new List<(string Core, int Start)>();
        foreach (var w in markup.Words) {
            var value = w.Value;
            var start = 0;
            while (start < value.Length && !char.IsLetterOrDigit(value[start]))
                start++;
            var end = value.Length;
            while (end > start && !char.IsLetterOrDigit(value[end - 1]))
                end--;
            if (end <= start)
                continue;

            words.Add((value[start..end].ToLower(), w.TextRange.Start + start));
        }
        if (words.Count == 0)
            return null;

        var (sentences, questions) = CountSentences(markup.Text);
        var repetitionSpans = new List<SpeechSpan>();
        for (var i = 1; i < words.Count; i++) {
            var (core, start) = words[i];
            if (core == words[i - 1].Core)
                repetitionSpans.Add(new SpeechSpan(SpeechSpanKind.Repetition, core, start, core.Length, ApiArray<string>.Empty));
        }

        return new SpeechTextStats(
            words.Count,
            sentences,
            questions,
            repetitionSpans.Count,
            words.Select(w => w.Core).Distinct().Count(),
            repetitionSpans.ToApiArray());
    }

    // Private methods

    private static (int Sentences, int Questions) CountSentences(string text)
    {
        var sentences = 0;
        var questions = 0;
        var hasContent = false;
        foreach (var c in text) {
            if (c is '.' or '!' or '?' or '\n') {
                if (hasContent) {
                    sentences++;
                    if (c == '?')
                        questions++;
                }
                hasContent = false;
            }
            else if (char.IsLetterOrDigit(c))
                hasContent = true;
        }
        if (hasContent)
            sentences++;
        return (sentences, questions);
    }
}
